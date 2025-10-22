using DeepDenoiseClient.Services.Common;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DeepDenoiseClient.Services.Alb;

public sealed class InvokeService
{
    private readonly HttpClient _http = new();
    private readonly SettingsService _settings;

    public string CorrelationHeaderName { get; set; } = "X-Request-Id";

    public InvokeService(SettingsService settings) { _settings = settings; }

    public async Task<(int Status, JsonDocument Body)> InvokeAsync(JsonElement body, string correlationId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, _settings.Active.InvokeUri)
        {
            Content = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation(CorrelationHeaderName, correlationId);

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var status = (int)resp.StatusCode;
        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        var doc = await JsonDocument.ParseAsync(s, cancellationToken: ct);
        return (status, doc);
    }
}
