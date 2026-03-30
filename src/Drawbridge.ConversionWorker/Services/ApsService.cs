using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Text;

namespace Drawbridge.ConversionWorker.Services
{
    public sealed class TranslationResult
    {
        public string Status { get; init; } = "";
        public Dictionary<string, string> ConfigViewableGuids { get; init; } = new();
    }

    public sealed class ApsService : IDisposable
    {
        private const string AuthBase  = "https://developer.api.autodesk.com/authentication/v2";
        private const string OssBase   = "https://developer.api.autodesk.com/oss/v2";
        private const string MdBase    = "https://developer.api.autodesk.com/modelderivative/v2";
        private const int    ChunkSize = 5 * 1024 * 1024;   // 5 MB (APS minimum)
        private const int    MaxPartsPerRequest = 25;

        private readonly WorkerSettings _settings;
        private readonly HttpClient     _http;
        private readonly SemaphoreSlim  _tokenLock = new(1, 1);
        private string?  _cachedToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        public ApsService(IOptions<WorkerSettings> settings)
        {
            _settings = settings.Value;
            _http     = new HttpClient();
        }

        // ── Auth ──────────────────────────────────────────────────────────────────

        public async Task<string> GetTokenAsync(CancellationToken ct = default)
        {
            await _tokenLock.WaitAsync(ct);
            try
            {
                if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry)
                    return _cachedToken;

                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{_settings.ApsClientId}:{_settings.ApsClientSecret}"));

                var req = new HttpRequestMessage(HttpMethod.Post, $"{AuthBase}/token");
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                req.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string,string>("grant_type", "client_credentials"),
                    new KeyValuePair<string,string>("scope",
                        "data:write data:read bucket:create bucket:read"),
                });

                var resp = await _http.SendAsync(req, ct);
                resp.EnsureSuccessStatusCode();

                var json      = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));
                var token     = json["access_token"]!.Value<string>()!;
                var expiresIn = json["expires_in"]!.Value<int>();

                _cachedToken = token;
                _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 300);
                return _cachedToken;
            }
            finally { _tokenLock.Release(); }
        }

        // ── OSS bucket ────────────────────────────────────────────────────────────

        public async Task EnsureBucketAsync(string bucketKey, ILogger logger, CancellationToken ct = default)
        {
            var token = await GetTokenAsync(ct);
            var req   = new HttpRequestMessage(HttpMethod.Post, $"{OssBase}/buckets");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(
                JsonConvert.SerializeObject(new { bucketKey, policyKey = "persistent" }),
                Encoding.UTF8, "application/json");

            var resp = await _http.SendAsync(req, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                logger.LogInformation("APS: bucket '{Key}' already exists", bucketKey);
                return;
            }
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException(
                    $"APS EnsureBucket failed ({(int)resp.StatusCode}): {body}");
            }
            logger.LogInformation("APS: bucket '{Key}' created", bucketKey);
        }

        // ── Upload ────────────────────────────────────────────────────────────────

        // Uploads via signed S3 multipart and returns the URL-safe base64 URN.
        public async Task<string> UploadAsync(
            string bucketKey, string objectKey, string filePath,
            ILogger logger, CancellationToken ct = default)
        {
            var fileInfo = new FileInfo(filePath);
            logger.LogInformation("APS upload: {File} ({Mb:F1} MB) → {Bucket}/{Key}",
                Path.GetFileName(filePath), fileInfo.Length / 1_048_576.0, bucketKey, objectKey);

            var objectId = await SignedUploadAsync(bucketKey, objectKey, filePath, fileInfo.Length, logger, ct);
            return EncodeUrn(objectId);
        }

        private async Task<string> SignedUploadAsync(
            string bucketKey, string objectKey, string filePath,
            long fileSize, ILogger logger, CancellationToken ct)
        {
            var token      = await GetTokenAsync(ct);
            var bucketEnc  = Uri.EscapeDataString(bucketKey);
            var keyEnc     = Uri.EscapeDataString(objectKey);
            int totalParts = (int)Math.Ceiling((double)fileSize / ChunkSize);

            logger.LogInformation("APS multipart: {Parts} part(s)", totalParts);

            string? uploadKey = null;
            var     eTags     = new List<string>(totalParts);

            using var fileStream = File.OpenRead(filePath);
            int partIndex = 0;

            while (partIndex < totalParts)
            {
                int batchSize = Math.Min(MaxPartsPerRequest, totalParts - partIndex);
                int firstPart = partIndex + 1;

                var urlQuery = $"?parts={batchSize}&firstPart={firstPart}&minutesExpiration=60"
                             + (uploadKey != null ? $"&uploadKey={Uri.EscapeDataString(uploadKey)}" : "");

                var signedReq = new HttpRequestMessage(HttpMethod.Get,
                    $"{OssBase}/buckets/{bucketEnc}/objects/{keyEnc}/signeds3upload{urlQuery}");
                signedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var signedResp = await _http.SendAsync(signedReq, ct);
                if (!signedResp.IsSuccessStatusCode)
                {
                    var body = await signedResp.Content.ReadAsStringAsync(ct);
                    throw new InvalidOperationException(
                        $"APS signeds3upload failed ({(int)signedResp.StatusCode}): {body}");
                }

                var signedJson = JObject.Parse(await signedResp.Content.ReadAsStringAsync(ct));
                uploadKey ??= signedJson["uploadKey"]!.Value<string>();
                var urls = signedJson["urls"]!.ToObject<string[]>()!;

                foreach (var url in urls)
                {
                    var buffer    = new byte[ChunkSize];
                    int bytesRead = await fileStream.ReadAsync(buffer, 0, ChunkSize, ct);
                    if (bytesRead == 0) break;

                    var s3Req = new HttpRequestMessage(HttpMethod.Put, url);
                    s3Req.Content = new ByteArrayContent(buffer, 0, bytesRead);

                    var s3Resp = await _http.SendAsync(s3Req, ct);
                    s3Resp.EnsureSuccessStatusCode();

                    var etag = s3Resp.Headers.ETag?.Tag
                               ?? s3Resp.Headers.GetValues("ETag").FirstOrDefault()
                               ?? throw new InvalidOperationException("S3 part upload returned no ETag");
                    eTags.Add(etag.Trim('"'));
                    partIndex++;
                }
            }

            var completeReq = new HttpRequestMessage(HttpMethod.Post,
                $"{OssBase}/buckets/{bucketEnc}/objects/{keyEnc}/signeds3upload");
            completeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            completeReq.Content = new StringContent(
                JsonConvert.SerializeObject(new { uploadKey, eTags }),
                Encoding.UTF8, "application/json");

            var completeResp = await _http.SendAsync(completeReq, ct);
            if (!completeResp.IsSuccessStatusCode)
            {
                var body = await completeResp.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException(
                    $"APS signeds3upload complete failed ({(int)completeResp.StatusCode}): {body}");
            }

            var completeJson = JObject.Parse(await completeResp.Content.ReadAsStringAsync(ct));
            return completeJson["objectId"]!.Value<string>()!;
        }

        // ── Model Derivative: translate ───────────────────────────────────────────

        public async Task TranslateAsync(string urnBase64, string rootFilename,
            string? configName = null, CancellationToken ct = default)
        {
            var token = await GetTokenAsync(ct);
            var body  = new
            {
                input = new
                {
                    urn           = urnBase64,
                    compressedUrn = true,
                    rootFilename,
                },
                output = new
                {
                    formats = new[]
                    {
                        new
                        {
                            type     = "svf2",
                            views    = new[] { "3d" },
                            advanced = configName != null
                                ? (object)new { specificConfiguration = configName }
                                : new { },
                        }
                    }
                }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, $"{MdBase}/designdata/job");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.TryAddWithoutValidation("x-ads-force", "true");
            req.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"APS translate failed ({(int)resp.StatusCode}): {err}");
            }
        }

        // Used for FBX / STL / SKP — no rootFilename, no specificConfiguration.
        public async Task TranslateFbxAsync(string urnBase64, CancellationToken ct = default)
        {
            var token = await GetTokenAsync(ct);
            var body  = new
            {
                input  = new { urn = urnBase64, compressedUrn = false },
                output = new { formats = new[] { new { type = "svf2", views = new[] { "3d" } } } }
            };

            var req = new HttpRequestMessage(HttpMethod.Post, $"{MdBase}/designdata/job");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.TryAddWithoutValidation("x-ads-force", "true");
            req.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException($"APS translate {(int)resp.StatusCode}: {err}");
            }
        }

        // ── Model Derivative: poll manifest ──────────────────────────────────────

        public async Task<TranslationResult> WaitForManifestAsync(
            string urnBase64, ILogger logger, CancellationToken ct = default)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var (status, configGuids) = await GetManifestAsync(urnBase64, ct);
                logger.LogInformation("APS manifest status: {Status}", status);

                if (status == "success")
                    return new TranslationResult { Status = status, ConfigViewableGuids = configGuids };
                if (status == "failed")
                    return new TranslationResult { Status = status };

                await Task.Delay(TimeSpan.FromSeconds(15), ct);
            }
        }

        private async Task<(string status, Dictionary<string, string> configGuids)>
            GetManifestAsync(string urnBase64, CancellationToken ct)
        {
            var token = await GetTokenAsync(ct);
            var req   = new HttpRequestMessage(HttpMethod.Get,
                $"{MdBase}/designdata/{Uri.EscapeDataString(urnBase64)}/manifest");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await _http.SendAsync(req, ct);

            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return ("pending", new Dictionary<string, string>());

            resp.EnsureSuccessStatusCode();

            var rawJson = await resp.Content.ReadAsStringAsync(ct);
            var json    = JObject.Parse(rawJson);
            var status  = json["status"]?.Value<string>() ?? "unknown";

            if (status != "pending" && status != "inprogress")
            {
                var safeUrn = urnBase64.Length > 12 ? urnBase64[..12] : urnBase64;
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), $"aps-manifest-{safeUrn}.json"),
                    rawJson);
            }

            var configGuids = new Dictionary<string, string>();
            foreach (var derivative in json["derivatives"] ?? Enumerable.Empty<JToken>())
                foreach (var child in derivative["children"] ?? Enumerable.Empty<JToken>())
                    CollectGeometryViewables(child, configGuids);

            return (status, configGuids);
        }

        // APS produces one geometry viewable per SolidWorks configuration. Recurse
        // into geometry containers and collect only leaf geometry nodes (the loadable ones).
        private static void CollectGeometryViewables(JToken node, Dictionary<string, string> dict)
        {
            if (node["type"]?.Value<string>() != "geometry" ||
                node["role"]?.Value<string>() != "3d")
                return;

            bool hasGeometryChildren = false;
            foreach (var child in node["children"] ?? Enumerable.Empty<JToken>())
            {
                if (child["type"]?.Value<string>() == "geometry" &&
                    child["role"]?.Value<string>() == "3d")
                {
                    hasGeometryChildren = true;
                    CollectGeometryViewables(child, dict);
                }
            }

            if (!hasGeometryChildren)
            {
                var name = node["name"]?.Value<string>();
                var guid = node["guid"]?.Value<string>();
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(guid))
                    dict[name] = guid;
            }
        }

        // ── OSS delete ────────────────────────────────────────────────────────────

        public async Task DeleteObjectAsync(string bucketKey, string urnBase64,
            ILogger logger, CancellationToken ct = default)
        {
            string objectKey;
            try
            {
                var pad     = (4 - urnBase64.Length % 4) % 4;
                var decoded = Encoding.UTF8.GetString(
                    Convert.FromBase64String(
                        urnBase64.Replace('-', '+').Replace('_', '/') + new string('=', pad)));
                var prefix  = $"urn:adsk.objects:os.object:{bucketKey}/";
                objectKey   = decoded.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? decoded[prefix.Length..]
                    : decoded.Split('/').Last();
            }
            catch
            {
                logger.LogWarning("APS delete: could not decode URN '{Urn}' — skipping", urnBase64);
                return;
            }

            var token    = await GetTokenAsync(ct);
            var req = new HttpRequestMessage(HttpMethod.Delete,
                $"{OssBase}/buckets/{Uri.EscapeDataString(bucketKey)}/objects/{Uri.EscapeDataString(objectKey)}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await _http.SendAsync(req, ct);
            if (resp.StatusCode is System.Net.HttpStatusCode.NotFound
                                 or System.Net.HttpStatusCode.NoContent
                || resp.IsSuccessStatusCode)
            {
                logger.LogInformation("APS: deleted object '{Key}'", objectKey);
                return;
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            logger.LogWarning("APS delete returned {Status} for '{Key}': {Body}",
                (int)resp.StatusCode, objectKey, body);
        }

        // ── Thumbnail ─────────────────────────────────────────────────────────────

        public async Task<byte[]?> DownloadThumbnailAsync(string urnBase64, CancellationToken ct = default)
        {
            var token = await GetTokenAsync(ct);
            var req   = new HttpRequestMessage(HttpMethod.Get,
                $"{MdBase}/designdata/{Uri.EscapeDataString(urnBase64)}/thumbnail?width=400&height=400");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsByteArrayAsync(ct);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        public static string EncodeUrn(string objectId) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(objectId))
                   .TrimEnd('=')
                   .Replace('+', '-')
                   .Replace('/', '_');

        public void Dispose() => _http.Dispose();
    }
}
