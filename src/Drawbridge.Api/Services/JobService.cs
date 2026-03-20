using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.SQS;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Drawbridge.Api.Services
{
    public class JobService
    {
        private readonly IAmazonSQS _sqs;
        private readonly IAmazonDynamoDB _dynamo;
        private readonly ApiSettings _settings;

        public JobService(IAmazonSQS sqs, IAmazonDynamoDB dynamo, IOptions<ApiSettings> settings)
        {
            _sqs = sqs;
            _dynamo = dynamo;
            _settings = settings.Value;
        }

        public async Task<string> SubmitJobAsync(SubmitJobRequest req)
        {
            var jobId = Guid.NewGuid().ToString();
            var now   = DateTime.UtcNow.ToString("o");

            await _dynamo.PutItemAsync(_settings.JobsTable, new Dictionary<string, AttributeValue>
            {
                ["jobId"]         = new AttributeValue(jobId),
                ["status"]        = new AttributeValue("queued"),
                ["vaultFilePath"] = new AttributeValue(req.VaultFilePath ?? ""),
                ["pdmVersion"]    = new AttributeValue { N = req.PdmVersion.ToString() },
                ["submittedBy"]   = new AttributeValue(req.SubmittedBy ?? ""),
                ["description"]   = new AttributeValue(req.Description ?? ""),
                ["createdAt"]     = new AttributeValue(now),
                ["updatedAt"]     = new AttributeValue(now),
            });

            var message = new
            {
                jobId,
                vaultName         = req.VaultName,
                vaultFilePath     = req.VaultFilePath,
                pdmVersion        = req.PdmVersion,
                configurations    = req.Configurations,
                fbxVaultPaths     = req.FbxVaultPaths,
                stlVaultPaths     = req.StlVaultPaths,
                skpVaultPaths     = req.SkpVaultPaths,
                submittedBy       = req.SubmittedBy,
                description       = req.Description,
                ownerName         = req.OwnerName,
                ownerEmail        = req.OwnerEmail,
                submittedAt       = now,
            };

            await _sqs.SendMessageAsync(_settings.SqsQueueUrl, JsonConvert.SerializeObject(message));

            return jobId;
        }

        public async Task<object?> GetJobAsync(string jobId)
        {
            var resp = await _dynamo.GetItemAsync(_settings.JobsTable,
                new Dictionary<string, AttributeValue>
                {
                    ["jobId"] = new AttributeValue(jobId),
                });

            if (!resp.IsItemSet) return null;

            var item = resp.Item;
            return new
            {
                jobId,
                status        = item.TryGetValue("status",        out var s)  ? s.S  : null,
                progress      = item.TryGetValue("progress",      out var pg) ? pg.S : null,
                vaultFilePath = item.TryGetValue("vaultFilePath", out var fp) ? fp.S : null,
                submittedBy   = item.TryGetValue("submittedBy",   out var sb) ? sb.S : null,
                createdAt     = item.TryGetValue("createdAt",     out var ca) ? ca.S : null,
                updatedAt     = item.TryGetValue("updatedAt",     out var ua) ? ua.S : null,
                errorMessage  = item.TryGetValue("errorMessage",  out var em) ? em.S : null,
            };
        }
    }
}
