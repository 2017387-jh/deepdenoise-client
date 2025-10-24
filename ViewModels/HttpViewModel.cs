using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDenoiseClient.Services.Alb;
using DeepDenoiseClient.Services.Common;
using DeepDenoiseClient.Services.Lambda;
using System.IO;
using System.Runtime.Intrinsics.Arm;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using DeepDenoiseClient.Models;

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
    [ObservableProperty] private string fileName = ""; // File Name
    [ObservableProperty] private string outputKey = "";
    [ObservableProperty] private string correlationId = Guid.NewGuid().ToString("N");
    [ObservableProperty] private int progressPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequestJsonEditable))]
    [NotifyPropertyChangedFor(nameof(RequestPreviewJson))]
    private InvokeRequestModel request = new();

    [ObservableProperty] private string responseJson = "";

    public string RequestPreviewJson => JsonSerializer.Serialize(Request, new JsonSerializerOptions { WriteIndented = true });

    [ObservableProperty] private string? requestJsonError;

    public string RequestJsonEditable
    {
        get => JsonSerializer.Serialize(Request, new JsonSerializerOptions { WriteIndented = true });
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                RequestJsonError = "빈 JSON 입니다.";
                return;
            }
            try
            {
                var parsed = JsonSerializer.Deserialize<InvokeRequestModel>(value);
                if (parsed is null) throw new InvalidOperationException("파싱 결과가 null 입니다.");
                Request = parsed;                  // 모델 갱신
                RequestJsonError = null;           // 에러 초기화
                OnPropertyChanged(nameof(RequestPreviewJson)); // 프리뷰 쓰면 갱신
            }
            catch (Exception ex)
            {
                RequestJsonError = ex.Message;     // 에러 표시
            }
        }
    }

    public HttpViewModel(SettingsService settings, PresignService presign, S3TransferService s3, InvokeService invoke, RunLogService logger)
    { 
        _settings = settings; 
        _presign = presign; 
        _s3 = s3; 
        _invoke = invoke; 
        _logger = logger; 
        
        RefreshFromSettings(); 
    }

    public void RefreshFromSettings()
    {
        ApiBase = _settings.ActiveProfile.ApiBase;

        Request = JsonSerializer.Deserialize<InvokeRequestModel>(
            JsonSerializer.Serialize(_settings.ActiveProfile.Defaults))!;
    }

    partial void OnFilePathChanged(string value)
    {
        // 보조: ObjectKey가 비어있다면 파일명으로 채워줌
        if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(FileName))
            FileName = Path.GetFileName(value);

        RebuildUrls();
    }

    partial void OnOutputKeyChanged(string value)
    {
        RebuildUrls();
    }

    [RelayCommand]
    private void ResetRequestToDefaults()
    {
        Request = JsonSerializer.Deserialize<InvokeRequestModel>(
            JsonSerializer.Serialize(_settings.ActiveProfile.Defaults))!;
        RequestJsonError = null;
        OnPropertyChanged(nameof(RequestPreviewJson));
    }

    [RelayCommand]
    private void PickFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.tif;*.tiff;*.png;*.jpg;*.jpeg|All|*.*" };
        if (dlg.ShowDialog() == true) 
        { 
            FilePath = dlg.FileName; 
            FileName = Path.GetFileName(FilePath); 
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task GetUploadUrlAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(FileName)) throw new InvalidOperationException("object-key가 비었습니다.");
        var (t, res) = await StopwatchExt.TimeAsync(() => _presign.GetUploadUrlAsync(FileName, ct));
        PresignedPutUrl = res.Url;
        _logger.WriteCsv(new RunRecord(DateTimeOffset.UtcNow, CorrelationId, FileName, OutputKey, new[] { new StepTiming("presign_upload", t, 200) }, true, null));
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task UploadAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(PresignedPutUrl) || string.IsNullOrWhiteSpace(FilePath)) return;
        var total = new FileInfo(FilePath).Length;
        var progress = new Progress<long>(sent => ProgressPercent = total > 0 ? (int)(sent * 100 / total) : 0);
        var (t, (st, bytes)) = await StopwatchExt.TimeAsync(async () => await _s3.UploadAsync(PresignedPutUrl, FilePath, progress, ct));
        _logger.WriteCsv(new RunRecord(DateTimeOffset.UtcNow, CorrelationId, FileName, OutputKey, new[] { new StepTiming("upload", t, st, bytes) }, true, null));
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task InvokeAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(RequestJsonError))
            throw new InvalidOperationException($"요청 JSON이 올바르지 않습니다: {RequestJsonError}");

        using var body = BuildInvokeBody(Request, _settings.ActiveProfile, FileName, FilePath);
        var (t, (st, rsp)) = await StopwatchExt.TimeAsync(async () => await _invoke.InvokeAsync(body.RootElement, CorrelationId, ct));

        ResponseJson = JsonSerializer.Serialize(rsp, new JsonSerializerOptions { WriteIndented = true });

        var outKey = TryPickOutputKeyFromS3Url(rsp);
        if (!string.IsNullOrWhiteSpace(outKey)) OutputKey = outKey!;

        _logger.WriteCsv(new RunRecord(DateTimeOffset.UtcNow, CorrelationId, FileName, OutputKey,
            new[] { new StepTiming("invoke", t, st) }, true, null));
        CorrelationId = Guid.NewGuid().ToString("N");
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task GetDownloadUrlAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(OutputKey)) throw new InvalidOperationException("output_key가 비었습니다.");
        var (t, res) = await StopwatchExt.TimeAsync(() => _presign.GetDownloadUrlAsync(OutputKey, ct));
        PresignedGetUrl = res.Url;
        _logger.WriteCsv(new RunRecord(DateTimeOffset.UtcNow, CorrelationId, FileName, OutputKey, new[] { new StepTiming("presign_download", t, 200) }, true, null));
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task DownloadAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(PresignedGetUrl)) return;
        var sfd = new Microsoft.Win32.SaveFileDialog { FileName = $"result_{OutputKey}" };
        if (sfd.ShowDialog() != true) return;
        var (t, (st, bytes)) = await StopwatchExt.TimeAsync(async () => await _s3.DownloadAsync(PresignedGetUrl, sfd.FileName, new Progress<long>(_ => { }), ct));
        _logger.WriteCsv(new RunRecord(DateTimeOffset.UtcNow, CorrelationId, FileName, OutputKey, new[] { new StepTiming("download", t, st, bytes) }, true, null));
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RunAllAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(RequestJsonError))
            throw new InvalidOperationException($"요청 JSON이 올바르지 않습니다: {RequestJsonError}");

        var started = DateTimeOffset.UtcNow; var steps = new List<StepTiming>(); var ok = false; string? err = null;
        var inKey = FileName; var outKey = OutputKey;

        try
        {
            if (string.IsNullOrWhiteSpace(FilePath)) throw new InvalidOperationException("파일을 선택하세요.");
            if (string.IsNullOrWhiteSpace(FileName)) FileName = Path.GetFileName(FilePath);
            inKey = FileName;

            var (t1, upPre) = await StopwatchExt.TimeAsync(() => _presign.GetUploadUrlAsync(FileName, ct));
            PresignedPutUrl = upPre.Url; steps.Add(new StepTiming("presign_upload", t1, 200));

            var total = new FileInfo(FilePath).Length;
            var progress = new Progress<long>(sent => ProgressPercent = total > 0 ? (int)(sent * 100 / total) : 0);
            var (t2, (stUp, bytesUp)) = await StopwatchExt.TimeAsync(async () => await _s3.UploadAsync(PresignedPutUrl, FilePath, progress, ct));
            steps.Add(new StepTiming("upload", t2, stUp, bytesUp));

            using var body = BuildInvokeBody(Request, _settings.ActiveProfile, inKey, FilePath);
            var (t3, (stInv, rsp)) = await StopwatchExt.TimeAsync(async () => await _invoke.InvokeAsync(body.RootElement, CorrelationId, ct));
            ResponseJson = JsonSerializer.Serialize(rsp, new JsonSerializerOptions { WriteIndented = true });
            steps.Add(new StepTiming("invoke", t3, stInv));

            outKey = TryPickOutputKeyFromS3Url(rsp) ?? outKey; OutputKey = outKey;

            var (t4, downPre) = await StopwatchExt.TimeAsync(() => _presign.GetDownloadUrlAsync(outKey, ct));
            PresignedGetUrl = downPre.Url; steps.Add(new StepTiming("presign_download", t4, 200));

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
    private static JsonDocument BuildInvokeBody(InvokeRequestModel req, ProfileInfo profileInfo, string objectKey, string filePath)
    {
        // 계정명 = 현재 선택된 프로필명
        var account = profileInfo.Name;

        // 파일명 결정: FileName가 비어있으면 filePath에서
        var fileName = !string.IsNullOrWhiteSpace(objectKey)
            ? objectKey
            : (!string.IsNullOrWhiteSpace(filePath) ? Path.GetFileName(filePath) : "");

        // URL 주입
        req.ImgInputUrl = fileName is null ? null : $"s3://{profileInfo.InBucket}/{fileName}";
        req.ImgOutputUrl = fileName is null ? null : $"s3://{profileInfo.OutBucket}/{fileName}";

        // 직렬화 → JsonDocument
        var bytes = JsonSerializer.SerializeToUtf8Bytes(req);
        return JsonDocument.Parse(bytes);
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

    private void RebuildUrls()
    {
        // 프로필/버킷/계정명
        var profile = _settings.ActiveProfile;
        var account = profile.Name;

        // input 파일명: ObjectKey 우선, 없으면 FilePath에서 추출
        var objectKey = !string.IsNullOrWhiteSpace(FileName)
            ? FileName
            : (!string.IsNullOrWhiteSpace(FilePath) ? Path.GetFileName(FilePath) : null);

        // S3 키 조합
        var s3InKey = string.IsNullOrWhiteSpace(objectKey) ? null : $"{account}/{objectKey}";
        var s3OutKey = string.IsNullOrWhiteSpace(objectKey) ? null : $"{account}/{objectKey}";

        // Request 안의 URL 업데이트
        Request.ImgInputUrl = s3InKey is null ? null : $"s3://{profile.InBucket}/{s3InKey}";
        Request.ImgOutputUrl = s3OutKey is null ? null : $"s3://{profile.OutBucket}/{s3OutKey}";

        // JSON 에디터/프리뷰 갱신 알림
        OnPropertyChanged(nameof(RequestJsonEditable));
        OnPropertyChanged(nameof(RequestPreviewJson));
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
