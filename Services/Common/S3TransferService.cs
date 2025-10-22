using System.IO;
using System.Net.Http;

namespace DeepDenoiseClient.Services.Common;

public sealed class S3TransferService
{
    private readonly HttpClient _http = new();

    public async Task<(int Status, long Bytes)> UploadAsync(string presignedPutUrl, string filePath, IProgress<long>? progress, CancellationToken ct)
    {
        using var fs = File.OpenRead(filePath);
        long total = fs.Length;
        using var content = new StreamContent(new ProgressStream(fs, progress));
        using var req = new HttpRequestMessage(HttpMethod.Put, presignedPutUrl) { Content = content };
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        return ((int)resp.StatusCode, total);
    }

    public async Task<(int Status, long Bytes)> DownloadAsync(string presignedGetUrl, string savePath, IProgress<long>? progress, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(presignedGetUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var rs = await resp.Content.ReadAsStreamAsync(ct);
        await using var fs = File.Create(savePath);
        var buf = new byte[81920];
        long total = 0; int n;
        while ((n = await rs.ReadAsync(buf.AsMemory(0, buf.Length), ct)) > 0)
        {
            await fs.WriteAsync(buf.AsMemory(0, n), ct);
            total += n;
            progress?.Report(total);
        }
        return ((int)resp.StatusCode, total);
    }
}
