using DeepDenoiseClient.Models;
using DeepDenoiseClient.Modesls;
using DeepDenoiseClient.Services.Common;
using System.Net.Http;
using System.Text.Json;

namespace DeepDenoiseClient.Services.Lambda;

public sealed class PresignService
{
    private readonly HttpClient _http = new();
    private readonly SettingsService _settings;

    public PresignService(SettingsService settings) { _settings = settings; }

    public Task<PresignResult> GetUploadUrlAsync(string objectKey, CancellationToken ct = default)
        => GetAsync("upload", objectKey, ct);

    public Task<PresignResult> GetDownloadUrlAsync(string objectKey, CancellationToken ct = default)
        => GetAsync("download", objectKey, ct);

    private async Task<PresignResult> GetAsync(string mode, string objectKey, CancellationToken ct)
    {
        var ub = new UriBuilder(_settings.Active.PresignUri)
        {
            Query = $"mode={Uri.EscapeDataString(mode)}&file={Uri.EscapeDataString(objectKey)}"
        };
        using var resp = await _http.GetAsync(ub.Uri, ct);
        resp.EnsureSuccessStatusCode();
        var text = await resp.Content.ReadAsStringAsync(ct);

        var trimmed = text.Trim('"', ' ', '\n', '\r', '\t');
        if (Uri.IsWellFormedUriString(trimmed, UriKind.Absolute))
            return new PresignResult(trimmed, mode == "upload" ? "PUT" : "GET");

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var url = root.TryGetProperty("url", out var u) ? u.GetString() : null;
        var method = root.TryGetProperty("method", out var m) ? m.GetString() : (mode == "upload" ? "PUT" : "GET");
        if (string.IsNullOrWhiteSpace(url)) throw new InvalidOperationException("Invalid presign response");
        return new PresignResult(url!, method ?? (mode == "upload" ? "PUT" : "GET"));
    }
}
