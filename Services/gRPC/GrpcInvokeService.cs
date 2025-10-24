using DeepDenoiseClient.Services.Common;
using Grpc.Core;
using Grpc.Net.Client;
using System.Text.Json;

using Ddn;  // proto 컴파일 후 생성된 네임스페이스(패키지명이 ddn이면)

namespace DeepDenoiseClient.Services.Grpc;

public sealed class GrpcInvokeService
{
    private readonly SettingsService _settings;

    public string CorrelationHeaderName { get; set; } = "x-request-id";

    public GrpcInvokeService(SettingsService settings) { _settings = settings; }

    public async Task<(ProcessReply Reply, StatusCode Code)> ProcessAsync(
        string inputKey,
        string? outputKey,
        JsonDocument extra,    // JSON 요청 추가 필드(모델/크기 등)에서 값 꺼내기 용도
        string correlationId,
        CancellationToken ct)
    {
        // 채널 생성 (ALB gRPC는 h2 over TLS)
        var endpoint = _settings.ActiveProfile.GrpcUri.ToString();
        var channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions
        {
            // 필요시 핸들러 커스터마이즈 (프록시/인증서 등)
        });

        // 클라이언트 스텁
        var client = new DeepDenoise.DeepDenoiseClient(channel);

        // extra JSON에서 필드 추출(없으면 기본값)
        string model = extra.RootElement.TryGetProperty("model", out var m) ? m.GetString() ?? "efficient" : "efficient";
        int width = extra.RootElement.TryGetProperty("width", out var w) ? w.GetInt32() : 3072;
        int height = extra.RootElement.TryGetProperty("height", out var h) ? h.GetInt32() : 3072;
        int usingBits = extra.RootElement.TryGetProperty("using_bits", out var b) ? b.GetInt32() : 16;

        var req = new ProcessRequest
        {
            InputKey = inputKey,
            OutputKey = outputKey ?? "",
            Model = model,
            Width = width,
            Height = height,
            UsingBits = usingBits
        };

        var headers = new Metadata { { CorrelationHeaderName, correlationId } };

        try
        {
            // 데드라인(타임아웃) 예: 60초
            var callOpts = new CallOptions(headers: headers, deadline: DateTime.UtcNow.AddSeconds(60), cancellationToken: ct);
            var reply = await client.ProcessAsync(req, callOpts);
            return (reply, StatusCode.OK);
        }
        catch (RpcException ex)
        {
            // 필요 시 로깅/재시도 정책 추가
            return (new ProcessReply { Status = ex.Status.Detail }, ex.StatusCode);
        }
        finally
        {
            await channel.ShutdownAsync();
        }
    }
}
