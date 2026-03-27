using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Drawbridge.Api.Services
{
    public class MarkupService
    {
        private readonly IAmazonDynamoDB _dynamo;
        private readonly IAmazonS3       _s3;
        private readonly ApiSettings     _settings;

        public MarkupService(IAmazonDynamoDB dynamo, IAmazonS3 s3, IOptions<ApiSettings> settings)
        {
            _dynamo   = dynamo;
            _s3       = s3;
            _settings = settings.Value;
        }

        public async Task<List<object>> ListAsync(string partNumber, int version)
        {
            var versionKey = $"{partNumber}#v{version}";

            var markupsTask = _dynamo.QueryAsync(new QueryRequest
            {
                TableName                 = _settings.MarkupsTable,
                IndexName                 = "ByVersion",
                KeyConditionExpression    = "versionKey = :vk",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":vk"] = new AttributeValue(versionKey),
                },
                ScanIndexForward = false,
            });

            // Fetch the persisted display order in parallel with the markup scan.
            var orderTask = _dynamo.GetItemAsync(new GetItemRequest
            {
                TableName = _settings.VersionsTable,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["partNumber"] = new AttributeValue(partNumber),
                    ["version"]    = new AttributeValue { N = version.ToString() },
                },
                ProjectionExpression = "markupOrder",
            });

            await Task.WhenAll(markupsTask, orderTask);

            var items         = markupsTask.Result.Items;
            var orderResponse = orderTask.Result;

            List<string>? savedOrder = null;
            if (orderResponse.IsItemSet
                && orderResponse.Item.TryGetValue("markupOrder", out var mo)
                && mo.L?.Count > 0)
            {
                savedOrder = mo.L.Select(v => v.S!).ToList();
            }

            if (savedOrder == null)
                return items.Select(item => (object)MapMarkup(item)).ToList();

            var map = items.ToDictionary(
                item => item.TryGetValue("markupId", out var mid) ? mid.S! : "",
                item => (object)MapMarkup(item));

            var result     = new List<object>();
            var orderedSet = new HashSet<string>(savedOrder);

            foreach (var id in savedOrder)
                if (map.TryGetValue(id, out var m)) result.Add(m);

            // Append markups not yet in the saved order (e.g. created concurrently)
            foreach (var (id, m) in map)
                if (!orderedSet.Contains(id)) result.Add(m);

            return result;
        }

        public async Task<object> CreateAsync(
            string  partNumber,
            int     version,
            string  previewDataUrl,
            string  markupSvg,
            string  viewerState,
            int     canvasWidth,
            int     canvasHeight,
            string  createdBy,
            string? title)
        {
            var markupId   = Guid.NewGuid().ToString();
            var createdAt  = DateTime.UtcNow.ToString("o");
            var versionKey = $"{partNumber}#v{version}";
            var bucket     = _settings.S3Bucket ?? throw new InvalidOperationException("S3Bucket not configured");
            var cfBase     = (_settings.CloudFrontBaseUrl ?? "").TrimEnd('/');

            var previewBase64 = previewDataUrl.Contains(',')
                ? previewDataUrl.Split(',')[1]
                : previewDataUrl;
            var previewBytes = Convert.FromBase64String(previewBase64);
            var previewKey   = $"products/{partNumber}/v{version}/markups/{markupId}/preview.jpg";

            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName  = bucket,
                Key         = previewKey,
                InputStream = new MemoryStream(previewBytes),
                ContentType = "image/jpeg",
            });

            var dataKey = $"products/{partNumber}/v{version}/markups/{markupId}/data.json";
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName  = bucket,
                Key         = dataKey,
                ContentBody = JsonSerializer.Serialize(new { markupSvg, viewerState }),
                ContentType = "application/json",
            });

            var item = new Dictionary<string, AttributeValue>
            {
                ["markupId"]    = new AttributeValue(markupId),
                ["versionKey"]  = new AttributeValue(versionKey),
                ["partNumber"]  = new AttributeValue(partNumber),
                ["version"]     = new AttributeValue { N = version.ToString() },
                ["previewUrl"]  = new AttributeValue($"{cfBase}/{previewKey}"),
                ["dataUrl"]     = new AttributeValue($"{cfBase}/{dataKey}"),
                ["canvasWidth"] = new AttributeValue { N = canvasWidth.ToString() },
                ["canvasHeight"]= new AttributeValue { N = canvasHeight.ToString() },
                ["createdBy"]   = new AttributeValue(createdBy),
                ["createdAt"]   = new AttributeValue(createdAt),
            };
            if (!string.IsNullOrEmpty(title))
                item["title"] = new AttributeValue(title);

            await _dynamo.PutItemAsync(_settings.MarkupsTable, item);
            await PrependToOrderAsync(partNumber, version, markupId);

            return MapMarkup(item);
        }

        public async Task DeleteAsync(string markupId, string partNumber, int version, string requestingUser)
        {
            var key = new Dictionary<string, AttributeValue>
            {
                ["markupId"] = new AttributeValue(markupId),
            };
            var existing = await _dynamo.GetItemAsync(_settings.MarkupsTable, key);
            if (existing.Item == null || existing.Item.Count == 0)
                throw new KeyNotFoundException($"Markup {markupId} not found");
            var owner = existing.Item.TryGetValue("createdBy", out var v) ? v.S : null;
            if (!string.Equals(owner, requestingUser, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Only the creator can delete this markup");

            await _dynamo.DeleteItemAsync(_settings.MarkupsTable, key);

            if (!string.IsNullOrEmpty(_settings.S3Bucket))
            {
                var prefix = $"products/{partNumber}/v{version}/markups/{markupId}/";
                await Task.WhenAll(
                    _s3.DeleteObjectAsync(_settings.S3Bucket, $"{prefix}preview.jpg"),
                    _s3.DeleteObjectAsync(_settings.S3Bucket, $"{prefix}data.json"));
            }
        }

        public async Task ReorderAsync(string partNumber, int version, IEnumerable<string> ids)
        {
            var orderList = ids.Select(id => new AttributeValue(id)).ToList();
            await _dynamo.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = _settings.VersionsTable,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["partNumber"] = new AttributeValue(partNumber),
                    ["version"]    = new AttributeValue { N = version.ToString() },
                },
                UpdateExpression = "SET markupOrder = :order",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":order"] = new AttributeValue { L = orderList, IsLSet = true },
                },
            });
        }

        private async Task PrependToOrderAsync(string partNumber, int version, string newId)
        {
            var orderResp = await _dynamo.GetItemAsync(new GetItemRequest
            {
                TableName = _settings.VersionsTable,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["partNumber"] = new AttributeValue(partNumber),
                    ["version"]    = new AttributeValue { N = version.ToString() },
                },
                ProjectionExpression = "markupOrder",
            });

            var current = new List<AttributeValue>();
            if (orderResp.IsItemSet
                && orderResp.Item.TryGetValue("markupOrder", out var mo)
                && mo.L != null)
            {
                current.AddRange(mo.L);
            }
            current.Insert(0, new AttributeValue(newId));

            await _dynamo.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = _settings.VersionsTable,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["partNumber"] = new AttributeValue(partNumber),
                    ["version"]    = new AttributeValue { N = version.ToString() },
                },
                UpdateExpression = "SET markupOrder = :order",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":order"] = new AttributeValue { L = current, IsLSet = true },
                },
            });
        }

        private static object MapMarkup(Dictionary<string, AttributeValue> item) => new
        {
            markupId     = item.TryGetValue("markupId",    out var mid) ? mid.S           : null,
            partNumber   = item.TryGetValue("partNumber",  out var pn)  ? pn.S            : null,
            version      = item.TryGetValue("version",     out var v)   ? int.Parse(v.N)  : 0,
            previewUrl   = item.TryGetValue("previewUrl",  out var pu)  ? pu.S            : null,
            dataUrl      = item.TryGetValue("dataUrl",     out var du)  ? du.S            : null,
            canvasWidth  = item.TryGetValue("canvasWidth", out var cw)  ? int.Parse(cw.N) : 0,
            canvasHeight = item.TryGetValue("canvasHeight",out var ch)  ? int.Parse(ch.N) : 0,
            createdBy    = item.TryGetValue("createdBy",   out var cb)  ? cb.S            : null,
            createdAt    = item.TryGetValue("createdAt",   out var ca)  ? ca.S            : null,
            title        = item.TryGetValue("title",       out var t)   ? t.S             : null,
        };
    }
}
