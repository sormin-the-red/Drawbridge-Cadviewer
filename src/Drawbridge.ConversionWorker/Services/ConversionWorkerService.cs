using Amazon.SQS;
using Amazon.SQS.Model;
using Drawbridge.Shared.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Drawbridge.ConversionWorker.Services
{
    public class ConversionWorkerService : BackgroundService
    {
        private readonly ILogger<ConversionWorkerService> _logger;
        private readonly WorkerSettings _settings;
        private readonly DynamoService  _dynamo;
        private readonly ApsService     _aps;
        private readonly S3Service      _s3;
        private readonly IAmazonSQS     _sqs;

        private const int SqsWaitTimeSeconds              = 20;
        private const int VisibilityExtendIntervalSeconds  = 600;

        public ConversionWorkerService(
            ILogger<ConversionWorkerService> logger,
            IOptions<WorkerSettings> settings,
            DynamoService dynamo,
            ApsService aps,
            S3Service s3)
        {
            _logger   = logger;
            _settings = settings.Value;
            _dynamo   = dynamo;
            _aps      = aps;
            _s3       = s3;
            _sqs      = new AmazonSQSClient(
                Amazon.RegionEndpoint.GetBySystemName(_settings.AwsRegion));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Conversion worker started. Polling {Queue}", _settings.SqsQueueUrl);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                    {
                        QueueUrl            = _settings.SqsQueueUrl,
                        MaxNumberOfMessages = 1,
                        WaitTimeSeconds     = SqsWaitTimeSeconds,
                        VisibilityTimeout   = 3600,
                    }, stoppingToken);

                    foreach (var message in response.Messages ?? [])
                        await ProcessMessageAsync(message, stoppingToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error polling SQS");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }

        private async Task ProcessMessageAsync(Message message, CancellationToken stoppingToken)
        {
            ConversionJob? job = null;
            try
            {
                job = JsonConvert.DeserializeObject<ConversionJob>(message.Body);
                _logger.LogInformation("Processing job {JobId}: {FilePath} v{Version}",
                    job!.JobId, job.VaultFilePath, job.PdmVersion);

                await _dynamo.UpdateJobStatusAsync(job.JobId, "processing");

                using var extendCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                try
                {
                    _ = ExtendVisibilityLoopAsync(message.ReceiptHandle, extendCts.Token);
                    await RunConversionAsync(job, stoppingToken);
                    await _sqs.DeleteMessageAsync(
                        _settings.SqsQueueUrl, message.ReceiptHandle, stoppingToken);
                    _logger.LogInformation("Job {JobId} complete", job.JobId);
                }
                finally { extendCts.Cancel(); }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed", job?.JobId);
                if (job != null)
                    await _dynamo.UpdateJobStatusAsync(job.JobId, "failed", ex.Message);
            }
        }

        private async Task RunConversionAsync(ConversionJob job, CancellationToken ct)
        {
            var workDir = Path.Combine(_settings.LocalWorkDir, job.JobId);
            Directory.CreateDirectory(workDir);

            try
            {
                // ── 1. PDM checkout ───────────────────────────────────────────────────────
                await _dynamo.UpdateJobProgressAsync(job.JobId, "Checking out files from PDM vault...");

                var checkout = PdmService.CheckOutFile(
                    _settings.VaultName, job.VaultFilePath, job.PdmVersion, workDir, _logger);

                _logger.LogInformation("PDM checkout: {Count} file(s), assembly at {Path}",
                    checkout.SyncedFilePaths.Count, checkout.AssemblyLocalPath);

                var partNumber = Path.GetFileNameWithoutExtension(checkout.AssemblyLocalPath);
                var configs    = job.Configurations ?? [];

                // ── 2. Ensure APS bucket ──────────────────────────────────────────────────
                await _aps.EnsureBucketAsync(_settings.ApsBucketKey, _logger, ct);

                // ── 3. Clean up old APS objects for this version (if re-submitting) ───────
                var existingUrns = await _dynamo.GetVersionConfigUrnsAsync(partNumber, job.PdmVersion);
                if (existingUrns?.Count > 0)
                {
                    await _dynamo.UpdateJobProgressAsync(job.JobId, "Cleaning up previous APS objects...");
                    foreach (var urn in existingUrns.Values.Distinct())
                        await _aps.DeleteObjectAsync(_settings.ApsBucketKey, urn, _logger, ct);
                }

                // ── 4. SolidWorks: activate + save each config in a single session ────────
                // All configs share one SW session so every configuration is fully resolved
                // on disk before we zip. Re-launching between configs leaves references unresolved.
                await _dynamo.UpdateJobProgressAsync(job.JobId,
                    $"Activating {configs.Length} configuration(s) in SolidWorks...");

                var swResult = SolidWorksConfigService.ProcessConfigs(
                    checkout.AssemblyLocalPath, configs,
                    _settings.VaultName, _settings.VaultRootPath, _logger);
                var succeededConfigs   = swResult.SucceededConfigs;
                var suppressedByConfig = swResult.SuppressedComponents;

                if (succeededConfigs.Count == 0)
                    throw new InvalidOperationException("No configurations could be activated in SolidWorks.");

                // Merge any component paths SW discovered that PDM's reference tree missed.
                var knownPaths = new HashSet<string>(checkout.SyncedFilePaths, StringComparer.OrdinalIgnoreCase);
                foreach (var compPath in swResult.AllComponentLocalPaths)
                {
                    if (knownPaths.Add(compPath))
                        checkout.SyncedFilePaths.Add(compPath);
                }

                // ── 5. Zip all files once (SW no longer needed) ───────────────────────────
                await _dynamo.UpdateJobProgressAsync(job.JobId, "Creating model archive...");

                var swZipPath       = Path.Combine(workDir, $"{partNumber}.zip");
                var assemblyRelPath = ZipPackService.CreateZip(
                    checkout.SyncedFilePaths,
                    _settings.VaultRootPath,
                    checkout.AssemblyLocalPath,
                    swZipPath,
                    _logger);

                // ── 6. Upload + translate (all configs share one URN) ─────────────────────
                // Per-config visual differences are handled via configSuppressedComponents;
                // no separate APS translations per config are needed.
                await _dynamo.UpdateJobProgressAsync(job.JobId, "Uploading model archive...");

                var swObjectKey = $"{partNumber}-v{job.PdmVersion}-{job.JobId}.zip";
                var sharedUrn   = await _aps.UploadAsync(
                    _settings.ApsBucketKey, swObjectKey, swZipPath, _logger, ct);

                await _dynamo.UpdateJobProgressAsync(job.JobId, "Translating model...");
                await _aps.TranslateAsync(sharedUrn, assemblyRelPath, ct: ct);

                await _dynamo.UpdateJobProgressAsync(job.JobId, "Waiting for translation...");
                TranslationResult sharedResult;
                using (var swPollCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    swPollCts.CancelAfter(TimeSpan.FromMinutes(30));
                    sharedResult = await _aps.WaitForManifestAsync(sharedUrn, _logger, swPollCts.Token);
                }

                var configViewableGuids = new Dictionary<string, string>();
                var configUrns          = new Dictionary<string, string>();
                string? firstUrn        = null;

                if (sharedResult.Status == "success")
                {
                    _logger.LogInformation("Shared translation viewables: {All}",
                        string.Join(", ", sharedResult.ConfigViewableGuids.Keys));

                    var sharedGuid = sharedResult.ConfigViewableGuids.Values.FirstOrDefault();
                    if (sharedGuid != null)
                    {
                        firstUrn = sharedUrn;
                        foreach (var config in succeededConfigs)
                        {
                            configViewableGuids[config] = sharedGuid;
                            configUrns[config]          = sharedUrn;
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Shared translation status: {Status}", sharedResult.Status);
                }

                if (configViewableGuids.Count == 0
                    && (job.FbxVaultPaths?.Length ?? 0) == 0
                    && (job.StlVaultPaths?.Length ?? 0) == 0
                    && (job.SkpVaultPaths?.Length ?? 0) == 0)
                    throw new InvalidOperationException("APS translation failed.");

                // ── 6a. FBX supplemental models ───────────────────────────────────────────
                foreach (var fbxVaultPath in job.FbxVaultPaths ?? [])
                {
                    var fbxFileName = Path.GetFileName(fbxVaultPath);
                    try
                    {
                        await _dynamo.UpdateJobProgressAsync(job.JobId,
                            $"Processing FBX: {fbxFileName}...");

                        var fbxLocalPath = PdmService.CheckOutSingleFile(
                            _settings.VaultName, fbxVaultPath, _logger);

                        var objectKey = $"{partNumber}-v{job.PdmVersion}-fbx-{MakeSafe(Path.GetFileNameWithoutExtension(fbxFileName))}-{job.JobId}{Path.GetExtension(fbxFileName).ToLower()}";
                        var fbxUrn    = await _aps.UploadAsync(
                            _settings.ApsBucketKey, objectKey, fbxLocalPath, _logger, ct);

                        await _aps.TranslateFbxAsync(fbxUrn, ct);

                        using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        pollCts.CancelAfter(TimeSpan.FromMinutes(30));
                        var result = await _aps.WaitForManifestAsync(fbxUrn, _logger, pollCts.Token);

                        if (result.Status == "success")
                        {
                            var guid = result.ConfigViewableGuids.Values.FirstOrDefault();
                            if (guid != null)
                            {
                                configViewableGuids[fbxFileName] = guid;
                                configUrns[fbxFileName]          = fbxUrn;
                                firstUrn                       ??= fbxUrn;
                                _logger.LogInformation("FBX '{Name}' → guid={Guid}", fbxFileName, guid);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("FBX '{Name}' translation {Status} — skipping",
                                fbxFileName, result.Status);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "FBX '{Name}' failed — skipping", fbxFileName);
                    }
                }

                // ── 6b. STL supplemental models ───────────────────────────────────────────
                foreach (var stlVaultPath in job.StlVaultPaths ?? [])
                {
                    var stlFileName = Path.GetFileName(stlVaultPath);
                    try
                    {
                        await _dynamo.UpdateJobProgressAsync(job.JobId,
                            $"Processing STL: {stlFileName}...");

                        var stlLocalPath = PdmService.CheckOutSingleFile(
                            _settings.VaultName, stlVaultPath, _logger);

                        var objectKey = $"{partNumber}-v{job.PdmVersion}-stl-{MakeSafe(Path.GetFileNameWithoutExtension(stlFileName))}-{job.JobId}{Path.GetExtension(stlFileName).ToLower()}";
                        var stlUrn    = await _aps.UploadAsync(
                            _settings.ApsBucketKey, objectKey, stlLocalPath, _logger, ct);

                        await _aps.TranslateFbxAsync(stlUrn, ct);

                        using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        pollCts.CancelAfter(TimeSpan.FromMinutes(30));
                        var result = await _aps.WaitForManifestAsync(stlUrn, _logger, pollCts.Token);

                        if (result.Status == "success")
                        {
                            var guid = result.ConfigViewableGuids.Values.FirstOrDefault();
                            if (guid != null)
                            {
                                configViewableGuids[stlFileName] = guid;
                                configUrns[stlFileName]          = stlUrn;
                                firstUrn                       ??= stlUrn;
                                _logger.LogInformation("STL '{Name}' → guid={Guid}", stlFileName, guid);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("STL '{Name}' translation {Status} — skipping",
                                stlFileName, result.Status);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "STL '{Name}' failed — skipping", stlFileName);
                    }
                }

                // ── 6c. SKP supplemental models ───────────────────────────────────────────
                foreach (var skpVaultPath in job.SkpVaultPaths ?? [])
                {
                    var skpFileName = Path.GetFileName(skpVaultPath);
                    try
                    {
                        await _dynamo.UpdateJobProgressAsync(job.JobId,
                            $"Processing SKP: {skpFileName}...");

                        var skpLocalPath = PdmService.CheckOutSingleFile(
                            _settings.VaultName, skpVaultPath, _logger);

                        var objectKey = $"{partNumber}-v{job.PdmVersion}-skp-{MakeSafe(Path.GetFileNameWithoutExtension(skpFileName))}-{job.JobId}{Path.GetExtension(skpFileName).ToLower()}";
                        var skpUrn    = await _aps.UploadAsync(
                            _settings.ApsBucketKey, objectKey, skpLocalPath, _logger, ct);

                        await _aps.TranslateFbxAsync(skpUrn, ct);

                        using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        pollCts.CancelAfter(TimeSpan.FromMinutes(30));
                        var result = await _aps.WaitForManifestAsync(skpUrn, _logger, pollCts.Token);

                        if (result.Status == "success")
                        {
                            var guid = result.ConfigViewableGuids.Values.FirstOrDefault();
                            if (guid != null)
                            {
                                configViewableGuids[skpFileName] = guid;
                                configUrns[skpFileName]          = skpUrn;
                                firstUrn                       ??= skpUrn;
                                _logger.LogInformation("SKP '{Name}' → guid={Guid}", skpFileName, guid);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("SKP '{Name}' translation {Status} — skipping",
                                skpFileName, result.Status);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "SKP '{Name}' failed — skipping", skpFileName);
                    }
                }

                if (configViewableGuids.Count == 0)
                    throw new InvalidOperationException("All APS translations failed.");

                // ── 7. Thumbnail ──────────────────────────────────────────────────────────
                string? thumbnailUrl = null;
                var cfBase = (_settings.CloudFrontBaseUrl ?? "").TrimEnd('/');
                if (!string.IsNullOrEmpty(_settings.S3Bucket) && !string.IsNullOrEmpty(cfBase)
                    && firstUrn != null)
                {
                    await _dynamo.UpdateJobProgressAsync(job.JobId, "Downloading thumbnail...");
                    var thumbBytes = await _aps.DownloadThumbnailAsync(firstUrn, ct);
                    if (thumbBytes != null)
                    {
                        var thumbKey = $"products/{partNumber}/v{job.PdmVersion}/thumbnail.jpg";
                        await _s3.UploadBytesAsync(
                            _settings.S3Bucket, thumbKey, thumbBytes, "image/jpeg", ct);
                        thumbnailUrl = $"{cfBase}/{thumbKey}";
                        _logger.LogInformation("Thumbnail uploaded: {Url}", thumbnailUrl);
                    }
                }

                // TODO (M9+): APS .slddrw → PDF translation
                bool hasDrawing = false;

                // ── 8. Persist to DynamoDB ────────────────────────────────────────────────
                var primaryUrn = configUrns.Values.FirstOrDefault() ?? firstUrn ?? "";

                await _dynamo.UpsertProductAsync(
                    partNumber, partNumber, job.PdmVersion, job.Description, thumbnailUrl);

                await _dynamo.UpsertVersionAsync(
                    partNumber, job.PdmVersion,
                    primaryUrn, configViewableGuids, configUrns, configViewableGuids.Keys.ToArray(),
                    suppressedByConfig, hasDrawing, job.SubmittedBy, thumbnailUrl,
                    job.OwnerName, job.OwnerEmail);

                await _dynamo.UpdateJobStatusAsync(job.JobId, "complete");
            }
            finally
            {
                try { Directory.Delete(workDir, recursive: true); } catch { }
            }
        }

        private static string MakeSafe(string name) =>
            System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9_\-]", "_");

        private async Task ExtendVisibilityLoopAsync(string receiptHandle, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(VisibilityExtendIntervalSeconds), ct);
                if (ct.IsCancellationRequested) break;
                try
                {
                    await _sqs.ChangeMessageVisibilityAsync(
                        _settings.SqsQueueUrl, receiptHandle, 3600, ct);
                }
                catch { }
            }
        }
    }
}
