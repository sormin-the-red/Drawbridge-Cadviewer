using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace Drawbridge.Api.Services
{
    public class AnnotationService
    {
        private readonly IAmazonDynamoDB _dynamo;
        private readonly ApiSettings     _settings;

        public AnnotationService(IAmazonDynamoDB dynamo, IOptions<ApiSettings> settings)
        {
            _dynamo   = dynamo;
            _settings = settings.Value;
        }

        public async Task<List<object>> ListAsync(string partNumber, int version)
        {
            var versionKey = $"{partNumber}#v{version}";
            var response = await _dynamo.QueryAsync(new QueryRequest
            {
                TableName                 = _settings.AnnotationsTable,
                IndexName                 = "ByVersion",
                KeyConditionExpression    = "versionKey = :vk",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":vk"] = new AttributeValue(versionKey),
                },
                ScanIndexForward = true,
            });
            return response.Items.Select(item => (object)MapAnnotation(item)).ToList();
        }

        public async Task<object> CreateAsync(
            string    partNumber,
            int       version,
            string[]? componentIds,
            double?   x,
            double?   y,
            double?   z,
            string    text,
            string    createdBy,
            string?   viewerState  = null,
            string?   parentId     = null,
            string?   markupSvg    = null,
            string[]? mentions     = null,
            string?   annotationId = null)
        {
            annotationId ??= Guid.NewGuid().ToString();
            var createdAt  = DateTime.UtcNow.ToString("o");
            var versionKey = $"{partNumber}#v{version}";

            var item = new Dictionary<string, AttributeValue>
            {
                ["annotationId"] = new AttributeValue(annotationId),
                ["versionKey"]   = new AttributeValue(versionKey),
                ["partNumber"]   = new AttributeValue(partNumber),
                ["version"]      = new AttributeValue { N = version.ToString() },
                ["text"]         = new AttributeValue(text),
                ["submittedBy"]  = new AttributeValue(createdBy),
                ["createdAt"]    = new AttributeValue(createdAt),
                ["resolved"]     = new AttributeValue { BOOL = false },
            };

            if (x.HasValue)
            {
                item["worldX"] = new AttributeValue { N = x.Value.ToString("G17") };
                item["worldY"] = new AttributeValue { N = y!.Value.ToString("G17") };
                item["worldZ"] = new AttributeValue { N = z!.Value.ToString("G17") };
            }
            if (componentIds?.Length > 0)
                item["componentIds"] = new AttributeValue { SS = componentIds.ToList() };
            if (!string.IsNullOrEmpty(viewerState))
                item["viewerState"] = new AttributeValue(viewerState);
            if (!string.IsNullOrEmpty(parentId))
                item["parentId"] = new AttributeValue(parentId);
            if (!string.IsNullOrEmpty(markupSvg))
                item["markupSvg"] = new AttributeValue(markupSvg);
            if (mentions?.Length > 0)
                item["mentions"] = new AttributeValue { SS = mentions.ToList() };

            await _dynamo.PutItemAsync(_settings.AnnotationsTable, item);
            return MapAnnotation(item);
        }

        public async Task<object> ToggleResolvedAsync(string annotationId)
        {
            var getResp = await _dynamo.GetItemAsync(_settings.AnnotationsTable,
                new Dictionary<string, AttributeValue>
                {
                    ["annotationId"] = new AttributeValue(annotationId),
                });

            if (getResp.Item == null || !getResp.Item.ContainsKey("annotationId"))
                throw new KeyNotFoundException(annotationId);

            var item           = getResp.Item;
            var currentResolved = item.TryGetValue("resolved", out var r) && r.BOOL.GetValueOrDefault();
            var newResolved     = !currentResolved;

            await _dynamo.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = _settings.AnnotationsTable,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["annotationId"] = new AttributeValue(annotationId),
                },
                UpdateExpression          = "SET #r = :v",
                ExpressionAttributeNames  = new Dictionary<string, string>   { ["#r"] = "resolved" },
                ExpressionAttributeValues = new Dictionary<string, AttributeValue> { [":v"] = new AttributeValue { BOOL = newResolved } },
            });

            item["resolved"] = new AttributeValue { BOOL = newResolved };
            return MapAnnotation(item);
        }

        public async Task DeleteAsync(string annotationId, string requestingUser)
        {
            var key = new Dictionary<string, AttributeValue>
            {
                ["annotationId"] = new AttributeValue(annotationId),
            };
            var existing = await _dynamo.GetItemAsync(_settings.AnnotationsTable, key);
            if (existing.Item == null || existing.Item.Count == 0)
                throw new KeyNotFoundException($"Annotation {annotationId} not found");
            var owner = existing.Item.TryGetValue("submittedBy", out var v) ? v.S : null;
            if (!string.Equals(owner, requestingUser, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Only the creator can delete this annotation");

            await _dynamo.DeleteItemAsync(_settings.AnnotationsTable, key);
        }

        private record WorldPosResult(double x, double y, double z);

        private static object MapAnnotation(Dictionary<string, AttributeValue> item) => new
        {
            annotationId  = item.TryGetValue("annotationId", out var id) ? id.S              : null,
            partNumber    = item.TryGetValue("partNumber",   out var pn) ? pn.S              : null,
            version       = item.TryGetValue("version",      out var v)  ? int.Parse(v.N)    : 0,
            componentIds  = item.TryGetValue("componentIds", out var ci) ? ci.SS.ToArray()   : Array.Empty<string>(),
            viewerState   = item.TryGetValue("viewerState",  out var vs) ? vs.S              : null,
            parentId      = item.TryGetValue("parentId",     out var pi) ? pi.S              : null,
            markupSvg     = item.TryGetValue("markupSvg",    out var ms) ? ms.S              : null,
            resolved      = item.TryGetValue("resolved",     out var res) && res.BOOL.GetValueOrDefault(),
            worldPosition = item.ContainsKey("worldX")
                ? new WorldPosResult(
                    double.Parse(item["worldX"].N),
                    double.Parse(item["worldY"].N),
                    double.Parse(item["worldZ"].N))
                : (WorldPosResult?)null,
            text        = item.TryGetValue("text",        out var t)  ? t.S              : null,
            submittedBy = item.TryGetValue("submittedBy", out var sb) ? sb.S             : null,
            createdAt   = item.TryGetValue("createdAt",   out var ca) ? ca.S             : null,
            mentions    = item.TryGetValue("mentions",    out var mn) ? mn.SS.ToArray()  : Array.Empty<string>(),
        };
    }
}
