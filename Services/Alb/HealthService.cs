using DeepDenoiseClient.Services.Common;
using System.Text.Json;
using System.Net.Http;

namespace DeepDenoiseClient.Services.Alb;

public sealed class HealthService
{
    private readonly HttpClient _http = new();
    private readonly SettingsService _settings;

    public HealthService(SettingsService settings) { _settings = settings; }

    public async Task<bool> CheckAsync(int timeoutMs = 1500)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            using var resp = await _http.GetAsync(_settings.ActiveProfile.HealthUri, cts.Token);
            if (!resp.IsSuccessStatusCode) return false;

            // {"status":"ok","timestamp":"2025-10-22T..."}
            await using var s = await resp.Content.ReadAsStreamAsync(cts.Token);
            using var doc = await JsonDocument.ParseAsync(s, cancellationToken: cts.Token);
            return doc.RootElement.TryGetProperty("status", out var st) &&
                   string.Equals(st.GetString(), "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
