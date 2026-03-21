using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace Drawbridge.Api.Services
{
    public class ProductService
    {
        private readonly IAmazonDynamoDB _dynamo;
        private readonly ApiSettings     _settings;

        public ProductService(IAmazonDynamoDB dynamo, IOptions<ApiSettings> settings)
        {
            _dynamo   = dynamo;
            _settings = settings.Value;
        }

        public async Task<List<object>> ListProductsAsync()
        {
            var response = await _dynamo.ScanAsync(new ScanRequest(_settings.ProductsTable));
            return response.Items.Select(MapProduct).ToList<object>();
        }

        public async Task<object?> GetProductWithVersionsAsync(string partNumber)
        {
            var productResp = await _dynamo.GetItemAsync(_settings.ProductsTable,
                new Dictionary<string, AttributeValue>
                {
                    ["partNumber"] = new AttributeValue(partNumber),
                });

            if (!productResp.IsItemSet) return null;

            var versionsResp = await _dynamo.QueryAsync(new QueryRequest
            {
                TableName              = _settings.VersionsTable,
                KeyConditionExpression = "partNumber = :pk",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pk"] = new AttributeValue(partNumber),
                },
                ScanIndexForward = false,
            });

            var versions = versionsResp.Items.Select(MapVersion).ToList<object>();
            var product  = MapProduct(productResp.Item);
            return new { product, versions };
        }

        private static object MapProduct(Dictionary<string, AttributeValue> item)
        {
            var partNumber    = item.TryGetValue("partNumber",    out var pn) ? pn.S           : null;
            var latestVersion = item.TryGetValue("latestVersion", out var lv) ? int.Parse(lv.N) : 0;

            return new
            {
                partNumber,
                name         = item.TryGetValue("name",         out var n)  ? n.S  : partNumber,
                description  = item.TryGetValue("description",  out var d)  ? d.S  : null,
                latestVersion,
                updatedAt    = item.TryGetValue("updatedAt",    out var ua) ? ua.S : null,
                thumbnailUrl = item.TryGetValue("thumbnailUrl", out var tu) ? tu.S : null,
            };
        }

        private static object MapVersion(Dictionary<string, AttributeValue> item)
        {
            var version    = item.TryGetValue("version", out var v) ? int.Parse(v.N) : 0;
            var apsUrn     = item.TryGetValue("apsUrn",  out var u) ? u.S            : null;
            var hasDrawing = item.TryGetValue("hasDrawing", out var hd) && hd.BOOL == true;

            var configurations = item.TryGetValue("configurations", out var cfgs)
                ? cfgs.L.Select(c => c.S).ToArray()
                : Array.Empty<string>();

            // Fall back to legacy single viewableGuid for records written before configViewableGuids existed.
            Dictionary<string, string?>? configViewableGuids = null;
            if (item.TryGetValue("configViewableGuids", out var cg) && cg.M?.Count > 0)
                configViewableGuids = cg.M.ToDictionary(kv => kv.Key, kv => kv.Value.S);
            else if (item.TryGetValue("viewableGuid", out var vg) && !string.IsNullOrEmpty(vg.S))
                configViewableGuids = new Dictionary<string, string?> { ["Scene"] = vg.S };

            Dictionary<string, string?>? configUrns = null;
            if (item.TryGetValue("configUrns", out var cu) && cu.M?.Count > 0)
                configUrns = cu.M.ToDictionary(kv => kv.Key, kv => kv.Value.S);

            Dictionary<string, string?[]>? configSuppressedComponents = null;
            if (item.TryGetValue("configSuppressedComponents", out var csc) && csc.M?.Count > 0)
                configSuppressedComponents = csc.M.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.L.Select(x => x.S).ToArray());

            return new
            {
                partNumber              = item.TryGetValue("partNumber",  out var pn) ? pn.S  : null,
                version,
                status                  = item.TryGetValue("status",      out var s)  ? s.S  : null,
                convertedAt             = item.TryGetValue("convertedAt", out var ca) ? ca.S : null,
                submittedBy             = item.TryGetValue("submittedBy", out var sb) ? sb.S : null,
                apsUrn,
                configViewableGuids,
                configUrns,
                configurations,
                configSuppressedComponents,
                hasDrawing,
                drawingUrl   = hasDrawing && item.TryGetValue("drawingUrl", out var du) ? du!.S : null,
                thumbnailUrl = item.TryGetValue("thumbnailUrl", out var tu) ? tu.S : null,
                ownerName    = item.TryGetValue("ownerName",    out var on) ? on.S : null,
                ownerEmail   = item.TryGetValue("ownerEmail",   out var oe) ? oe.S : null,
            };
        }
    }
}
