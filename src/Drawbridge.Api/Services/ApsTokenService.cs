using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Drawbridge.Api.Services
{
    public class ApsTokenService
    {
        private readonly ApiSettings  _settings;
        private readonly HttpClient   _http = new();
        private readonly SemaphoreSlim _lock = new(1, 1);

        private string?  _cachedToken;
        private DateTime _expiresAt = DateTime.MinValue;

        private const string TokenUrl = "https://developer.api.autodesk.com/authentication/v2/token";

        public ApsTokenService(IOptions<ApiSettings> settings)
        {
            _settings = settings.Value;
        }

        // Returns a viewer-scoped token and remaining lifetime in seconds.
        // Cached across Lambda warm invocations; refreshed 5 minutes before expiry.
        public async Task<(string Token, int ExpiresIn)> GetViewerTokenAsync(
            CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct);
            try
            {
                var now = DateTime.UtcNow;
                if (_cachedToken != null && now < _expiresAt)
                    return (_cachedToken, (int)(_expiresAt - now).TotalSeconds);

                var credentials = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{_settings.ApsClientId}:{_settings.ApsClientSecret}"));

                var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "client_credentials",
                        ["scope"]      = "viewables:read",
                    })
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

                var resp = await _http.SendAsync(req, ct);
                resp.EnsureSuccessStatusCode();

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                var token     = doc.RootElement.GetProperty("access_token").GetString()!;
                var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();

                _cachedToken = token;
                _expiresAt   = now.AddSeconds(expiresIn - 300);

                return (token, expiresIn);
            }
            finally { _lock.Release(); }
        }
    }
}
