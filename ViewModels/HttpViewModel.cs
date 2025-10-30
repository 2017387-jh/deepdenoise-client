using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDenoiseClient.Models;
using DeepDenoiseClient.Services.Alb;
using DeepDenoiseClient.Services.Common;
using DeepDenoiseClient.Services.Lambda;
using DeepDenoiseClient.Utils;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.Intrinsics.Arm;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Input;

namespace DeepDenoiseClient.ViewModels;

public partial class HttpViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly PresignService _presign;
    private readonly S3TransferService _s3;
    private readonly InvokeService _invoke;
    private readonly RunLogService _logger;
    private readonly IStatusbarService _statusbar;

    [ObservableProperty] private string apiBase = "";
    [ObservableProperty] private string presignedPutUrl = "";
    [ObservableProperty] private string presignedGetUrl = "";
    [ObservableProperty] private string localFilePath = "";

    [ObservableProperty]
    private ObservableCollection<string> localFilePaths = new();

    [ObservableProperty] private string fileName = ""; // File Name
    [ObservableProperty] private string objectKey = "";
    [ObservableProperty] private string correlationId = Guid.NewGuid().ToString("N");
    [ObservableProperty] private int uploadProgressPercent;
    [ObservableProperty] private int downloadProgressPercent;



    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequestJsonEditable))]
    [NotifyPropertyChangedFor(nameof(RequestPreviewJson))]
    private InvokeRequestModel request = new();

    [ObservableProperty] private string responseJson = "";

    public string RequestPreviewJson => JsonSerializer.Serialize(Request, new JsonSerializerOptions { WriteIndented = true });

    [ObservableProperty] private string? requestJsonError;

    [ObservableProperty]
    private int runAllTotalCount;
    [ObservableProperty]
    private int runAllCompletedCount;
    public string RunAllProgressText => $"{RunAllCompletedCount}/{RunAllTotalCount}";

    partial void OnRunAllCompletedCountChanged(int value)
    {
        OnPropertyChanged(nameof(RunAllProgressText));
    }
    partial void OnRunAllTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(RunAllProgressText));
    }

    public int LocalFilePathsCount => LocalFilePaths?.Count ?? 0;
    partial void OnLocalFilePathsChanged(ObservableCollection<string> value)
    {
        OnPropertyChanged(nameof(LocalFilePathsCount));
        if (value != null)
            value.CollectionChanged += (_, __) => OnPropertyChanged(nameof(LocalFilePathsCount));
    }

    public string RequestJsonEditable
    {
        get => JsonSerializer.Serialize(Request, new JsonSerializerOptions { WriteIndented = true });
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                RequestJsonError = "Empty JSON";
                return;
            }
            try
            {
                var parsed = JsonSerializer.Deserialize<InvokeRequestModel>(value);
                if (parsed is null) throw new InvalidOperationException("Parsed result is null.");
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

    public HttpViewModel(SettingsService settings, 
        PresignService presign, 
        S3TransferService s3, 
        InvokeService invoke, 
        RunLogService logger,
        IStatusbarService statusbar)
    { 
        _settings = settings; 
        _presign = presign; 
        _s3 = s3; 
        _invoke = invoke; 
        _logger = logger;
        _statusbar = statusbar;

        RefreshFromSettings(); 
    }

    public void RefreshFromSettings()
    {
        ApiBase = _settings.ActiveProfile.ApiBase;

        Request = JsonSerializer.Deserialize<InvokeRequestModel>(
            JsonSerializer.Serialize(_settings.ActiveProfile.Defaults))!;

        UpdateObjectKey();
        RebuildRequestJSON();
    }

    partial void OnFileNameChanged(string value)
    {
        UpdateObjectKey();
        RebuildRequestJSON();
    }

    partial void OnLocalFilePathChanged(string value)
    {
        // 보조: ObjectKey가 비어있다면 파일명으로 채워줌
        if (!string.IsNullOrWhiteSpace(value))
            FileName = Path.GetFileName(value);

        RebuildRequestJSON();


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
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "RAW/TIFF|*.raw;*.tif;*.tiff|All|*.*",
            Multiselect = true
        };

        if (dlg.ShowDialog() == true)
        {
            LocalFilePaths = new ObservableCollection<string>(dlg.FileNames);
            // 첫 번째 파일을 기존 단일 프로퍼티에도 반영 (호환성)
            LocalFilePath = dlg.FileNames.FirstOrDefault() ?? "";
            FileName = Path.GetFileName(LocalFilePath);
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task GetUploadUrlAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(FileName))
        {
            throw new InvalidOperationException($"object-key(file name) is empty");
        }

        using (_statusbar.Begin())
        {
            var account = _settings.ActiveProfile.Name; // 사용자/계정명
            var key = ObjectKey;
            var (t, res) = await StopwatchExt.TimeAsync(() => _presign.GetUploadUrlAsync(key, ct));

            PresignedPutUrl = res.Url;
            _logger.Info($"presign upload ok cid={CorrelationId}", path: $"/presign(upload)", elapsed: t, httpStatus: 200);

            _statusbar.Success("Upload URL has been generated");
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task UploadAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(LocalFilePath) || !File.Exists(LocalFilePath))
        {
            _logger.Warn("There is no file to upload.");
            return;
        }

        if (string.IsNullOrWhiteSpace(PresignedPutUrl))
        {
            _logger.Warn("Presigned url is assigned");
            return;
        }

        using (_statusbar.Begin())
        {

            try
            {
                var total = new FileInfo(LocalFilePath).Length;
                var progress = new Progress<long>(sent => UploadProgressPercent = total > 0 ? (int)(sent * 100 / total) : 0);
                var (t, (st, bytes)) = await StopwatchExt.TimeAsync(async () => await _s3.UploadAsync(PresignedPutUrl, LocalFilePath, progress, ct));
                
                _logger.Info($"upload ok cid={CorrelationId}", path: "upload", elapsed: t, httpStatus: st, bytes: bytes);

                _statusbar.Success("Upload completed");
            }
            catch (OperationCanceledException)
            {
                _statusbar.Fail("Upload canceled");
                _logger.Warn("Upload canceled", path: "http/upload");
            }
            catch (System.Exception ex)
            {
                _statusbar.Fail("Upload failed");
                _logger.Error("Upload failed", ex, path: "http/upload");
            }
        }


    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task InvokeAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(RequestJsonError))
            throw new InvalidOperationException($"요청 JSON이 올바르지 않습니다: {RequestJsonError}");

        using (_statusbar.Begin())
        {

            using var body = BuildInvokeBody(Request, _settings.ActiveProfile, FileName);
            var (t, (st, rsp)) = await StopwatchExt.TimeAsync(async () => await _invoke.InvokeAsync(body.RootElement, CorrelationId, ct));

            ResponseJson = JsonSerializer.Serialize(rsp, new JsonSerializerOptions { WriteIndented = true });

            _logger.Info($"invoke ok cid={CorrelationId}", path: $"/invacations", elapsed: t, httpStatus: st);
            CorrelationId = Guid.NewGuid().ToString("N");

            _statusbar.Success("Invocation is completed");
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task GetDownloadUrlAsync(CancellationToken ct)
    {
        using(_statusbar.Begin())
        {
            if (string.IsNullOrWhiteSpace(ObjectKey))
            {
                MessageBox.Show("Output Key is empty. Please specify the output key before generating the download URL.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // ObjectKey는 이미 "account/파일명" 형태여야 함
            var (t, res) = await StopwatchExt.TimeAsync(() => _presign.GetDownloadUrlAsync(ObjectKey, ct));

            PresignedGetUrl = res.Url;
            _logger.Info($"presign download ok cid={CorrelationId}", path: $"/presign(download)", elapsed: t, httpStatus: 200);

            _statusbar.Success("Download URL has been generated");
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task DownloadAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(PresignedGetUrl)) return;

        using (_statusbar.Begin())
        {
            try
            {
                var downloadRoot = Path.Combine(AppContext.BaseDirectory, "download");
                // outKey 내 구분자를 OS 기준으로 맞춰서 경로 생성
                var destPath = Path.Combine(downloadRoot, ObjectKey.Replace('/', Path.DirectorySeparatorChar)
                                                               .Replace('\\', Path.DirectorySeparatorChar));

                // 하위 디렉터리까지 생성
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                var total = await _s3.GetContentLengthAsync(PresignedGetUrl, ct);

                var progress = new Progress<long>(received =>
                {
                    if (total > 0)
                        DownloadProgressPercent = (int)(received * 100 / total);
                });

                // Download
                var (t5, (stDown, bytesDown)) = await StopwatchExt.TimeAsync(async () =>
                    await _s3.DownloadAsync(PresignedGetUrl, destPath, progress, ct));

                // Finish as 100 %
                DownloadProgressPercent = 100;

                _logger.Info($"download ok cid={CorrelationId} -> {destPath}", path: $"/download", elapsed: t5, httpStatus: stDown, bytes: bytesDown);

                _statusbar.Success("Download completed");
            }
            catch (OperationCanceledException)
            {
                _statusbar.Fail("Download canceled");
                _logger.Warn("Download canceled", path: "http/download");
            }
            catch (System.Exception ex)
            {
                _statusbar.Fail("Download failed");
                _logger.Error("Download failed", ex, path: "http/download");
            }
        }
    }

    [RelayCommand]
    private void OpenDownloadFolder()
    {
        var downloadRoot = Path.Combine(AppContext.BaseDirectory, "download");
        if (!Directory.Exists(downloadRoot))
            Directory.CreateDirectory(downloadRoot);

        // Windows 탐색기로 폴더 열기
        Process.Start(new ProcessStartInfo
        {
            FileName = downloadRoot,
            UseShellExecute = true
        });
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task RunAllAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(RequestJsonError))
            throw new InvalidOperationException($"JSON is invalid: {RequestJsonError}");

        var files = LocalFilePaths?.Any() == true ? LocalFilePaths : new ObservableCollection<string> { LocalFilePath };

        RunAllTotalCount = files.Count;
        RunAllCompletedCount = 0;

        foreach (var file in files)
        {
            if (ct.IsCancellationRequested) break;

            LocalFilePath = file;
            FileName = Path.GetFileName(file);
            UpdateObjectKey();
            RebuildRequestJSON();

            var started = Stopwatch.GetTimestamp();

            using (_statusbar.Begin())
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(LocalFilePath)) throw new InvalidOperationException("Select a file.");
                    if (string.IsNullOrWhiteSpace(FileName)) FileName = Path.GetFileName(LocalFilePath);

                    var (t1, upPre) = await StopwatchExt.TimeAsync(() => _presign.GetUploadUrlAsync(ObjectKey, ct));

                    PresignedPutUrl = upPre.Url;
                    _logger.Info($"presign upload ok cid={CorrelationId}", path: $"/presign(upload)", elapsed: t1, httpStatus: 200);

                    var uploadTotal = new FileInfo(LocalFilePath).Length;
                    var uploadProgress = new Progress<long>(sent => UploadProgressPercent = uploadTotal > 0 ? (int)(sent * 100 / uploadTotal) : 0);
                    var (t2, (stUp, bytesUp)) = await StopwatchExt.TimeAsync(async () => await _s3.UploadAsync(PresignedPutUrl, LocalFilePath, uploadProgress, ct));
                    _logger.Info($"upload ok cid={CorrelationId}", path: $"/upload", elapsed: t2, httpStatus: stUp, bytes: bytesUp);

                    using var body = BuildInvokeBody(Request, _settings.ActiveProfile, ObjectKey);
                    var (t3, (stInv, rsp)) = await StopwatchExt.TimeAsync(async () => await _invoke.InvokeAsync(body.RootElement, CorrelationId, ct));
                    ResponseJson = JsonSerializer.Serialize(rsp, new JsonSerializerOptions { WriteIndented = true });
                    _logger.Info($"invoke ok cid={CorrelationId}", path: $"/invocations", elapsed: t3, httpStatus: stInv);


                    var (t4, downPre) = await StopwatchExt.TimeAsync(() => _presign.GetDownloadUrlAsync(ObjectKey, ct));
                    PresignedGetUrl = downPre.Url;
                    _logger.Info($"presign download ok cid={CorrelationId}", path: $"/presign(download)", elapsed: t4, httpStatus: 200);

                    var downloadRoot = Path.Combine(AppContext.BaseDirectory, "download");
                    // outKey 내 구분자를 OS 기준으로 맞춰서 경로 생성
                    var destPath = Path.Combine(downloadRoot, ObjectKey.Replace('/', Path.DirectorySeparatorChar)
                                                                   .Replace('\\', Path.DirectorySeparatorChar));

                    // 하위 디렉터리까지 생성
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                    // 다운로드
                    DownloadProgressPercent = 0;
                    var downloadTotal = await _s3.GetContentLengthAsync(PresignedGetUrl, ct);
                    var downloadProgress = new Progress<long>(received =>
                    {
                        if (downloadTotal > 0)
                            DownloadProgressPercent = (int)(received * 100 / downloadTotal);
                    });

                    var (t5, (stDown, bytesDown)) = await StopwatchExt.TimeAsync(async () =>
                        await _s3.DownloadAsync(PresignedGetUrl, destPath, downloadProgress, ct));

                    DownloadProgressPercent = 100;
                    _logger.Info($"download ok cid={CorrelationId} -> {destPath}",
                                 path: $"/download", elapsed: t5, httpStatus: stDown, bytes: bytesDown);

                    _statusbar.Success("All Steps are completed");
                }
                catch (Exception ex)
                {
                    var totalElapsed = Stopwatch.GetElapsedTime(started); // 총 소요 시간

                    _logger.Error($"run all failed cid={CorrelationId}", ex, path: $"/run_all", elapsed: totalElapsed, 503);
                    CorrelationId = Guid.NewGuid().ToString("N");
                    throw;
                }
                finally
                {
                    var totalElapsed = Stopwatch.GetElapsedTime(started); // 총 소요 시간

                    _logger.Info($"run all completed cid={CorrelationId}", path: $"/run_all", elapsed: totalElapsed, 200);
                    CorrelationId = Guid.NewGuid().ToString("N");

                    RunAllCompletedCount++;
                }
            }
        }
    }

    // helpers
    private static JsonDocument BuildInvokeBody(InvokeRequestModel req, ProfileInfo profileInfo, string objectKey)
    {
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

    private void RebuildRequestJSON()
    {
        // 프로필/버킷/계정명
        var profile = _settings.ActiveProfile;
        var account = profile.Name;

        // input 파일명: ObjectKey 우선, 없으면 FilePath에서 추출
        var objectKey = !string.IsNullOrWhiteSpace(FileName)
            ? FileName
            : (!string.IsNullOrWhiteSpace(LocalFilePath) ? Path.GetFileName(LocalFilePath) : null);

        // TIFF 파일이면 width/height 자동 업데이트
        if (!string.IsNullOrWhiteSpace(LocalFilePath))
        {
            var ext = Path.GetExtension(LocalFilePath).ToLowerInvariant();
            if (ext == ".tif" || ext == ".tiff")
            {
                try
                {
                    // System.Drawing.Common 패키지 필요 (Windows 환경)
                    using var img = System.Drawing.Image.FromFile(LocalFilePath);
                    Request.Width = img.Width;
                    Request.Height = img.Height;
                }
                catch (Exception ex)
                {
                    // 실패 시 무시 또는 로그
                    _logger.Warn($"TIFF 크기 추출 실패: {ex.Message}");
                }
            }
        }

        if (string.IsNullOrWhiteSpace(objectKey))
        {
            Request.ImgInputUrl = profile.Defaults.ImgInputUrl;
            Request.ImgOutputUrl = profile.Defaults.ImgOutputUrl;

            FileName = PathUtil.GetFileNameFromUrlOrPath(Request.ImgInputUrl) == null
                ? ""
                : PathUtil.GetFileNameFromUrlOrPath(Request.ImgInputUrl)!;

            if (string.IsNullOrWhiteSpace(LocalFilePath))
            {
                LocalFilePath = Path.Combine(AppContext.BaseDirectory, FileName);
            }
        }
        else
        {
            // S3 키 조합
            var s3InKey = string.IsNullOrWhiteSpace(objectKey) ? null : $"{account}/{objectKey}";
            var s3OutKey = string.IsNullOrWhiteSpace(objectKey) ? null : $"{account}/{objectKey}";

            // Request 안의 URL 업데이트
            Request.ImgInputUrl = s3InKey is null ? null : $"s3://{profile.InBucket}/{s3InKey}";
            Request.ImgOutputUrl = s3OutKey is null ? null : $"s3://{profile.OutBucket}/{s3OutKey}";
        }

        // JSON 에디터/프리뷰 갱신 알림
        OnPropertyChanged(nameof(RequestJsonEditable));
        OnPropertyChanged(nameof(RequestPreviewJson));
    }

    private void UpdateObjectKey()
    {
        if (!string.IsNullOrWhiteSpace(FileName) && !string.IsNullOrWhiteSpace(_settings.ActiveProfile?.Name))
            ObjectKey = $"{_settings.ActiveProfile.Name}/{FileName}";
        else
            ObjectKey = "";
    }

    private static string? TryPickOutputKey(JsonDocument d)
    {
        if (d.RootElement.TryGetProperty("output_key", out var v)) return v.GetString();
        if (d.RootElement.TryGetProperty("result_s3_key", out var v2)) return v2.GetString(); return null;
    }
    private static string? TryPickOutputImgUrl(JsonDocument d)
    {
        if (d.RootElement.TryGetProperty("img_output_url", out var v)) return v.GetString();
        else return "";
    }
}
