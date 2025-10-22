using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDenoiseClient.Services.Alb;
using DeepDenoiseClient.Services.Common;
using DeepDenoiseClient.Services.Lambda;
using System.IO;
using System.Runtime.Intrinsics.Arm;
using System.Text.Json;

namespace DeepDenoiseClient.ViewModels;

public partial class HttpViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly PresignService _presign;
    private readonly S3TransferService _s3;
    private readonly InvokeService _invoke;
    private readonly RunLogService _logger;

    [ObservableProperty] private string apiBase = "";
    [ObservableProperty] private string presignedPutUrl = "";
    [ObservableProperty] private string presignedGetUrl = "";
    [ObservableProperty] private string filePath = "";
    [ObservableProperty] private string objectKey = "";
    [ObservableProperty] private string outputKey = "";
    [ObservableProperty] private string correlationId = Guid.NewGuid().ToString("N");
    [ObservableProperty] private int progressPercent;
    [ObservableProperty]
    private string requestJson =
        "{\n" +
        "  \"model\": \"efficient\",\n" +
        "  \"pixel_pitch\": 140,\n" +
        "  \"type\": \"static\",\n" +
        "  \"strength\": 20,\n" +
        "  \"width\": 3072,\n" +
        "  \"height\": 3072,\n" +
        "  \"using_bits\": 16,\n" +
        "  \"digital_offset\": 100\n" +
        "}";
    [ObservableProperty] private string responseJson = "";

    public HttpViewModel(SettingsService settings, PresignService presign, S3TransferService s3, InvokeService invoke, RunLogService logger)
    { _settings = settings; _presign = presign; _s3 = s3; _invoke = invoke; _logger = logger; RefreshFromSettings(); }

    public void RefreshFromSettings() => ApiBase = _settings.Active.ApiBase;

    [RelayCommand]
    private void PickFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.tif;*.tiff;*.png;*.jpg;*.jpeg|All|*.*" };
        if (dlg.ShowDialog() == true) { FilePath = dlg.FileName; ObjectKey = Path.GetFileName(FilePath); }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task GetUploadUrlAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ObjectKey)) throw new InvalidOperationException("object-key가 비었습니다.");
        var (t, res) = await StopwatchExt.TimeAsync(() => _presign.GetUploadUrlAsync(ObjectKey, ct));
        PresignedPutUrl = res.Url;
        _logger.WriteCsv(new RunRecord(DateTimeOffset.UtcNow, CorrelationId, ObjectKey, OutputKey, new[] { new StepTiming("presign_upload", t, 200) }, true, null));
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task UploadAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(PresignedPutUrl) || string.IsNullOrWhiteSpace(FilePath)) return;
        var total = new FileInfo(FilePath).Length;
        var progress = new Progress<long>(sent => ProgressPercent = total > 0 ? (int)(sent * 100 / total) : 0);
        var (t, (st, bytes)) = await StopwatchExt.TimeAsync(async () => await _s3.UploadAsync(PresignedPutUrl, FilePath, progress, ct));
        _logger.WriteCsv(new RunRecord(DateTimeOffset.UtcNow, CorrelationId, ObjectKey, OutputKey, new[] { new StepTiming("upload", t, st, bytes) }, true, null));
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task InvokeAsync(CancellationToken ct)
    {
        using var body = BuildInvokeBody(RequestJson, ObjectKey, OutputKey);
        var (t, (st, rsp)) = await StopwatchExt.TimeAsync(async () => await _invoke.InvokeAsync(body.RootElement, CorrelationId, ct));
        ResponseJson = JsonSerializer.Serialize(rsp, new JsonSerializerOptions { WriteIndented = true });

        // img_output_url -> OutputKey 추출
        var outKey = TryPickOutputKeyFromS3Url(rsp);
        if (!string.IsNullOrWhiteSpace(outKey)) OutputKey = outKey!;

        _logger.WriteCsv(new RunRecord(DateTimeOffset.UtcNow, CorrelationId, ObjectKey, OutputKey,
            new[] { new StepTiming("invoke", t, st) }, true, null));
        CorrelationId = Guid.NewGuid().ToString("N");
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task GetDownloadUrlAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(OutputKey)) throw new InvalidOperationException("output_key가 비었습니다.");
        var (t, res) = await StopwatchExt.TimeAsync(() => _presign.GetDownloadUrlAsync(OutputKey, ct));
        PresignedGetUrl = res.Url;
        _logger.WriteCsv(new RunRecord(DateTimeOffset.UtcNow, CorrelationId, ObjectKey, OutputKey, new[] { new StepTiming("presign_download", t, 200) }, true, null));
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task DownloadAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(PresignedGetUrl)) return;
        var sfd = new Microsoft.Win32.SaveFileDialog { FileName = $"result_{OutputKey}" };
        if (sfd.ShowDialog() != true) return;
        var (t, (st, bytes)) = await StopwatchExt.TimeAsync(async () => await _s3.DownloadAsync(PresignedGetUrl, sfd.FileName, new Progress<long>(_ => { }), ct));
        _logger.WriteCsv(new RunRecord(DateTimeOffset.UtcNow, CorrelationId, ObjectKey, OutputKey, new[] { new StepTiming("download", t, st, bytes) }, true, null));
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RunAllAsync(CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow; var steps = new List<StepTiming>(); var ok = false; string? err = null;
        var inKey = ObjectKey; var outKey = OutputKey;

        try
        {
            if (string.IsNullOrWhiteSpace(FilePath)) throw new InvalidOperationException("파일을 선택하세요.");
            if (string.IsNullOrWhiteSpace(ObjectKey)) ObjectKey = Path.GetFileName(FilePath);
            inKey = ObjectKey;

            // 1) 업로드 Presign
            var (t1, upPre) = await StopwatchExt.TimeAsync(() => _presign.GetUploadUrlAsync(ObjectKey, ct));
            PresignedPutUrl = upPre.Url; steps.Add(new StepTiming("presign_upload", t1, 200));

            // 2) 업로드
            var total = new FileInfo(FilePath).Length;
            var progress = new Progress<long>(sent => ProgressPercent = total > 0 ? (int)(sent * 100 / total) : 0);
            var (t2, (stUp, bytesUp)) = await StopwatchExt.TimeAsync(async () => await _s3.UploadAsync(PresignedPutUrl, FilePath, progress, ct));
            steps.Add(new StepTiming("upload", t2, stUp, bytesUp));

            // 3) Invoke (img_input_url/img_output_url 추가)
            using var body = BuildInvokeBody(RequestJson, inKey, outKey);
            var (t3, (stInv, rsp)) = await StopwatchExt.TimeAsync(async () => await _invoke.InvokeAsync(body.RootElement, CorrelationId, ct));
            ResponseJson = JsonSerializer.Serialize(rsp, new JsonSerializerOptions { WriteIndented = true });
            steps.Add(new StepTiming("invoke", t3, stInv));

            // 응답의 img_output_url 에서 OutputKey 추출
            outKey = TryPickOutputKeyFromS3Url(rsp) ?? outKey; OutputKey = outKey;

            // 4) 다운로드 URL(사전 제공 X) → Presign(download)
            var (t4, downPre) = await StopwatchExt.TimeAsync(() => _presign.GetDownloadUrlAsync(outKey, ct));
            PresignedGetUrl = downPre.Url; steps.Add(new StepTiming("presign_download", t4, 200));

            // 5) 다운로드
            var sfd = new Microsoft.Win32.SaveFileDialog { FileName = $"result_{outKey}" };
            if (sfd.ShowDialog() == true)
            {
                var (t5, (stDown, bytesDown)) = await StopwatchExt.TimeAsync(async () => await _s3.DownloadAsync(PresignedGetUrl, sfd.FileName, new Progress<long>(_ => { }), ct));
                steps.Add(new StepTiming("download", t5, stDown, bytesDown));
            }
            ok = true;
        }
        catch (Exception ex) { err = ex.Message; }
        finally
        {
            _logger.WriteCsv(new RunRecord(started, CorrelationId, inKey, outKey, steps, ok, err));
            CorrelationId = Guid.NewGuid().ToString("N");
        }
    }

    // helpers
    private static JsonDocument BuildInvokeBody(string baseJson, string inputKey, string outputKey)
    {
        // s3://<bucket>/<key> 형식으로 조합
        var inputUrl = $"s3://ddn-in-bucket/{inputKey}";
        var outputUrl = string.IsNullOrWhiteSpace(outputKey) ? null : $"s3://ddn-out-bucket/{outputKey}";

        using var baseDoc = JsonDocument.Parse(baseJson);
        using var ms = new MemoryStream();
        using var w = new Utf8JsonWriter(ms);

        w.WriteStartObject();
        foreach (var p in baseDoc.RootElement.EnumerateObject()) p.WriteTo(w);
        w.WriteString("img_input_url", inputUrl);
        if (!string.IsNullOrWhiteSpace(outputUrl)) w.WriteString("img_output_url", outputUrl);
        w.WriteEndObject();

        w.Flush(); ms.Position = 0;
        return JsonDocument.Parse(ms);
    }

    private static string? TryPickOutputKeyFromS3Url(JsonDocument d)
    {
        if (!d.RootElement.TryGetProperty("img_output_url", out var v)) return null;
        var s = v.GetString();
        if (string.IsNullOrWhiteSpace(s)) return null;

        // s3://bucket/key... → key... 로 변환
        // 예: s3://ddn-out-bucket/user/output_xxx.tif  → user/output_xxx.tif
        const string prefix = "s3://";
        if (s!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var idx = s.IndexOf('/', prefix.Length); // 버킷 다음 '/' 위치
            if (idx > 0 && idx + 1 < s.Length)
                return s[(idx + 1)..]; // key 부분
        }
        return null;
    }

    private static string? TryPickOutputKey(JsonDocument d)
    {
        if (d.RootElement.TryGetProperty("output_key", out var v)) return v.GetString();
        if (d.RootElement.TryGetProperty("result_s3_key", out var v2)) return v2.GetString(); return null;
    }
    private static string? TryPickOutputUrl(JsonDocument d)
    {
        if (d.RootElement.TryGetProperty("output_url", out var v)) return v.GetString();
        if (d.RootElement.TryGetProperty("result_s3_url", out var v2)) return v2.GetString(); return null;
    }
}
