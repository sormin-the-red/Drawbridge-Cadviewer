using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Options;

namespace Drawbridge.ConversionWorker.Services
{
    public class DynamoService
    {
        private readonly IAmazonDynamoDB _dynamo;
        private readonly WorkerSettings  _settings;

        public DynamoService(IOptions<WorkerSettings> settings)
        {
            _settings = settings.Value;
            _dynamo   = new AmazonDynamoDBClient(
                Amazon.RegionEndpoint.GetBySystemName(_settings.AwsRegion));
        }

        // Returns the configUrns map from the existing version record, or null if none exists.
        // Used to clean up stale APS objects when a version is re-submitted.
        public async Task<Dictionary<string, string>?> GetVersionConfigUrnsAsync(
            string partNumber, int version)
        {
            var resp = await _dynamo.GetItemAsync(_settings.DynamoVersionsTable,
                new Dictionary<string, AttributeValue>
                {
                    ["partNumber"] = new AttributeValue(partNumber),
                    ["version"]    = new AttributeValue { N = version.ToString() },
                });

            if (!resp.IsItemSet) return null;

            if (resp.Item.TryGetValue("configUrns", out var cu) && cu.M?.Count > 0)
                return cu.M.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.S!);

            // Legacy: single apsUrn field with no per-config map
            if (resp.Item.TryGetValue("apsUrn", out var u) && !string.IsNullOrEmpty(u.S))
                return new Dictionary<string, string> { ["_legacy"] = u.S! };

            return null;
        }

        public async Task UpdateJobStatusAsync(string jobId, string status, string? errorMessage = null)
        {
            var updates = new Dictionary<string, AttributeValueUpdate>
            {
                ["status"]    = new AttributeValueUpdate(new AttributeValue(status), AttributeAction.PUT),
                ["updatedAt"] = new AttributeValueUpdate(
                    new AttributeValue(DateTime.UtcNow.ToString("o")), AttributeAction.PUT),
            };

            if (errorMessage != null)
                updates["errorMessage"] = new AttributeValueUpdate(
                    new AttributeValue(errorMessage), AttributeAction.PUT);

            await _dynamo.UpdateItemAsync(_settings.DynamoJobsTable,
                new Dictionary<string, AttributeValue> { ["jobId"] = new AttributeValue(jobId) },
                updates);
        }

        public async Task UpdateJobProgressAsync(string jobId, string message)
        {
            await _dynamo.UpdateItemAsync(_settings.DynamoJobsTable,
                new Dictionary<string, AttributeValue> { ["jobId"] = new AttributeValue(jobId) },
                new Dictionary<string, AttributeValueUpdate>
                {
                    ["progress"]  = new AttributeValueUpdate(new AttributeValue(message), AttributeAction.PUT),
                    ["updatedAt"] = new AttributeValueUpdate(
                        new AttributeValue(DateTime.UtcNow.ToString("o")), AttributeAction.PUT),
                });
        }

        public async Task UpsertProductAsync(
            string partNumber, string name, int latestVersion,
            string? description = null, string? thumbnailUrl = null)
        {
            var item = new Dictionary<string, AttributeValue>
            {
                ["partNumber"]    = new AttributeValue(partNumber),
                ["name"]          = new AttributeValue(name),
                ["description"]   = new AttributeValue(description ?? ""),
                ["latestVersion"] = new AttributeValue { N = latestVersion.ToString() },
                ["updatedAt"]     = new AttributeValue(DateTime.UtcNow.ToString("o")),
            };
            if (!string.IsNullOrEmpty(thumbnailUrl))
                item["thumbnailUrl"] = new AttributeValue(thumbnailUrl);

            await _dynamo.PutItemAsync(_settings.DynamoProductsTable, item);
        }

        public async Task UpsertVersionAsync(
            string partNumber, int version,
            string apsUrn,
            Dictionary<string, string> configViewableGuids,
            Dictionary<string, string> configUrns,
            string[] configurations,
            IReadOnlyDictionary<string, List<string>> configSuppressedComponents,
            bool hasDrawing, string submittedBy, string? thumbnailUrl = null,
            string? ownerName = null, string? ownerEmail = null)
        {
            var configList = new AttributeValue
            {
                L = (configurations ?? Array.Empty<string>())
                    .Select(c => new AttributeValue(c)).ToList()
            };

            var configGuidsAttr = new AttributeValue
            {
                M = (configViewableGuids ?? new Dictionary<string, string>())
                    .ToDictionary(kvp => kvp.Key, kvp => new AttributeValue(kvp.Value))
            };

            var configUrnsAttr = new AttributeValue
            {
                M = (configUrns ?? new Dictionary<string, string>())
                    .ToDictionary(kvp => kvp.Key, kvp => new AttributeValue(kvp.Value))
            };

            var suppressedAttr = new AttributeValue
            {
                M = (configSuppressedComponents ?? new Dictionary<string, List<string>>())
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => new AttributeValue { L = kvp.Value.Select(s => new AttributeValue(s)).ToList() })
            };

            var item = new Dictionary<string, AttributeValue>
            {
                ["partNumber"]                 = new AttributeValue(partNumber),
                ["version"]                    = new AttributeValue { N = version.ToString() },
                ["status"]                     = new AttributeValue("complete"),
                ["apsUrn"]                     = new AttributeValue(apsUrn),
                ["configViewableGuids"]         = configGuidsAttr,
                ["configUrns"]                 = configUrnsAttr,
                ["configurations"]             = configList,
                ["configSuppressedComponents"] = suppressedAttr,
                ["hasDrawing"]                 = new AttributeValue { BOOL = hasDrawing },
                ["submittedBy"]                = new AttributeValue(submittedBy ?? ""),
                ["convertedAt"]                = new AttributeValue(DateTime.UtcNow.ToString("o")),
            };
            if (!string.IsNullOrEmpty(thumbnailUrl))
                item["thumbnailUrl"] = new AttributeValue(thumbnailUrl);
            if (!string.IsNullOrEmpty(ownerName))
                item["ownerName"] = new AttributeValue(ownerName);
            if (!string.IsNullOrEmpty(ownerEmail))
                item["ownerEmail"] = new AttributeValue(ownerEmail);

            await _dynamo.PutItemAsync(_settings.DynamoVersionsTable, item);
        }
    }
}
