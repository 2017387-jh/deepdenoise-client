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

    public string CorrelationHeaderName { get; set; } = "X-Request-Id";

    public InvokeService(SettingsService settings) { _settings = settings; }

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

            // 상관관계 헤더
            req.Headers.TryAddWithoutValidation(CorrelationHeaderName, correlationId);

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            status = (int)resp.StatusCode;

            // 응답 본문 읽기
            var text = await resp.Content.ReadAsStringAsync(ct);

            if (string.IsNullOrWhiteSpace(text))
                return (status, JsonDocument.Parse("{\"error\":\"Empty response body\"}"));

            // JSON 파싱 시도
            try
            {
                var doc = JsonDocument.Parse(text);
                return (status, doc);
            }
            catch
            {
                // 비JSON 응답일 때도 안전하게 감싸서 반환
                var safe = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
                var err = JsonDocument.Parse($"{{\"error\":\"Non-JSON response\",\"raw\":\"{safe}\"}}");
                return (status, err);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var msg = ex.Message.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var err = JsonDocument.Parse($"{{\"error\":\"{msg}\"}}");
            return (500, err);
        }
    }
}
