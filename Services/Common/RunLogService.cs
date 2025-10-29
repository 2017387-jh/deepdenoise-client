using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using static Grpc.Core.Metadata;
using DeepDenoiseClient.Models;

namespace DeepDenoiseClient.Services.Common;
public sealed class RunLogService
{
    private readonly string _dir = Path.Combine(AppContext.BaseDirectory, "logs");
    private static readonly object _fileLock = new();

    public event Action<LogModel>? Entry;

    public RunLogService() { Directory.CreateDirectory(_dir); }

    // 화면/VM으로 흘려보내는 한 줄 로그
    public event Action<string>? Line;

    private static string HttpStatusToString(int code)
    {
        return code switch
        {
            200 => "OK",
            201 => "Created",
            202 => "Accepted",
            204 => "No Content",
            400 => "Bad Request",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            409 => "Conflict",
            500 => "Internal Server Error",
            502 => "Bad Gateway",
            503 => "Service Unavailable",
            504 => "Gateway Timeout",
            _ => ""
        };
    }

    // 호출 예: Info("업로드 완료", path: "upload", elapsed: sw.Elapsed, httpStatus: 200, bytes: size);
    public void Info(string message, string? path = null, TimeSpan? elapsed = null, int? httpStatus = null, long? bytes = null)
        => Emit("INFO", message, path, elapsed, httpStatus, bytes);

    public void Warn(string message, string? path = null, TimeSpan? elapsed = null, int? httpStatus = null, long? bytes = null)
        => Emit("WARN", message, path, elapsed, httpStatus, bytes);

    public void Error(string message, Exception? ex = null, string? path = null, TimeSpan? elapsed = null, int? httpStatus = null, long? bytes = null)
    {
        if (ex != null) message = $"{message} | {ex.GetType().Name}: {ex.Message}";
        Emit("ERROR", message, path, elapsed, httpStatus, bytes);
    }

    private void Emit(string level, string msg, string? path, TimeSpan? elapsed, int? httpStatus, long? bytes)
    {
        var sb = new StringBuilder();
        sb.Append($"{DateTime.Now:HH:mm:ss.fff} [{level}] [{path}] {msg}");

        // 동적으로 값이 있을 때만 추가
        if (elapsed.HasValue)
            sb.Append($" - elapsed={(elapsed.Value.TotalMilliseconds / 1000).ToString("F2")}s");
        if (httpStatus.HasValue)
        {
            var statusStr = HttpStatusToString(httpStatus.Value);
            sb.Append($" - httpStatus={httpStatus.Value}({(string.IsNullOrEmpty(statusStr) ? "" : statusStr)})");
        }

        double kbytes = 0.0f;

        if (bytes.HasValue)
            kbytes = bytes.Value/1024;

        var line = sb.ToString();

        try { Line?.Invoke(line); } catch { /* swallow */ }
        Debug.WriteLine(line);

        var entry = new LogModel
        {
            Timestamp = DateTimeOffset.Now,
            Level = level,
            Path = path ?? "",
            ElapsedMs = elapsed?.TotalMilliseconds,
            HttpStatus = httpStatus,
            KiloBytes = kbytes,
            Message = msg
        };
        try { Entry?.Invoke(entry); } catch { }

        AppendCsv(new CsvRow(
            Ts: DateTimeOffset.Now,
            Level: level,
            Path: path,
            ElapsedMs: elapsed?.TotalMilliseconds,
            HttpStatus: httpStatus,
            KiloBytes: bytes,
            Message: msg
        ));
    }

    private readonly record struct CsvRow(
        DateTimeOffset Ts,
        string Level,
        string? Path,
        double? ElapsedMs,
        int? HttpStatus,
        double? KiloBytes,
        string Message);

    private void AppendCsv(CsvRow row)
    {
        try
        {
            var file = Path.Combine(_dir, $"runs_{DateTime.UtcNow:yyyyMMdd}.csv");
            var newFile = !File.Exists(file);

            var sb = new StringBuilder();
            if (newFile)
                sb.AppendLine("ts,level,path,elapsed_ms,http_status,bytes,message");

            sb.AppendLine(string.Join(",",
                Csv(row.Ts.ToString("o")),
                Csv(row.Level),
                Csv(row.Path),
                row.ElapsedMs?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.HttpStatus?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.KiloBytes?.ToString(CultureInfo.InvariantCulture) ?? "",
                Csv(row.Message)
            ));

            lock (_fileLock)
            {
                File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RunLogService CSV write failed: {ex}");
        }
    }

    private static string Csv(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
}

public static class StopwatchExt
{
    public static async Task<(TimeSpan Elapsed, T Result)> TimeAsync<T>(Func<Task<T>> func)
    {
        var sw = Stopwatch.StartNew();
        var res = await func();
        sw.Stop();
        return (sw.Elapsed, res);
    }
}
