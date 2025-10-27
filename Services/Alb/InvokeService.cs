using DeepDenoiseClient.Services.Common;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DeepDenoiseClient.Services.Alb;

public sealed class InvokeService
{
    private readonly HttpClient _http = new();
    private readonly SettingsService _settings;
    private readonly RunLogService _log;

    public string CorrelationHeaderName { get; set; } = "X-Request-Id";

    public InvokeService(SettingsService settings, RunLogService log) { _settings = settings; _log = log; }

    public async Task<(int Status, JsonDocument Body)> InvokeAsync(JsonElement body, string correlationId, CancellationToken ct)
    {
        var status = 500;
        var fallback = JsonDocument.Parse("{\"error\":\"Unknown error\"}");
        var bodyJson = body.GetRawText();

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, _settings.ActiveProfile.InvokeUri);

            // 본문 설정
            var bytes = Encoding.UTF8.GetBytes(bodyJson);
            req.Content = new ByteArrayContent(bytes);

            // 서버가 charset을 싫어하는 경우 대비: charset 없는 application/json
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            // 선택: 응답을 JSON으로 받고 싶다는 의사 표시
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            req.Headers.TryAddWithoutValidation(CorrelationHeaderName, correlationId);

            var (elapsed, resp) = await StopwatchExt.TimeAsync(() =>
                _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct));

            status = (int)resp.StatusCode;
            var text = await resp.Content.ReadAsStringAsync(ct);

            _log.Info($"Status {status} in {elapsed.TotalMilliseconds:F0} ms, respLen={(text?.Length ?? 0)}");

            if (string.IsNullOrWhiteSpace(text))
                return (status, JsonDocument.Parse("{\"error\":\"Empty response body\"}"));

            // JSON 파싱 시도
            try
            {
                var doc = JsonDocument.Parse(text);
                return (status, doc);
            }
            catch (Exception ex)
            {
                // 비JSON 응답일 때도 안전하게 감싸서 반환
                var safe = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
                var err = JsonDocument.Parse($"{{\"error\":\"Non-JSON response\",\"raw\":\"{safe}\"}}");

                _log.Error($"Response is not JSON. {ex.GetType().Name}: {ex.Message}");

                return (status, err);
            }
        }
        catch (OperationCanceledException)
        {
            _log.Warn("Invoke canceled.");

            throw;
        }
        catch (Exception ex)
        {
            _log.Error("Invoke failed.", ex);

            var msg = ex.Message.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var err = JsonDocument.Parse($"{{\"error\":\"{msg}\"}}");
            return (500, err);
        }
    }
}
