using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace DeepDenoiseClient.Services.Common;

public record StepTiming(string Name, TimeSpan Elapsed, int? HttpStatus = null, long? Bytes = null);
public record RunRecord(DateTimeOffset StartedAt, string CorrelationId, string InputKey, string OutputKey,
                        IReadOnlyList<StepTiming> Steps, bool Success, string? Error);

public sealed class RunLogService
{
    private readonly string _dir = Path.Combine(AppContext.BaseDirectory, "logs");
    public RunLogService() { Directory.CreateDirectory(_dir); }

    public void WriteCsv(RunRecord rec)
    {
        try
        {
            var file = Path.Combine(_dir, $"runs_{DateTime.UtcNow:yyyyMMdd}.csv");
            var newFile = !File.Exists(file);
            var sb = new StringBuilder();
            if (newFile)
                sb.AppendLine("started_at,correlation_id,input_key,output_key,success,error," +
                              "t_presign_up_ms,t_upload_ms,t_invoke_ms,t_presign_down_ms,t_download_ms," +
                              "st_presign_up,st_upload,st_invoke,st_presign_down,st_download," +
                              "bytes_up,bytes_down,total_ms");

            long? bUp = rec.Steps.FirstOrDefault(s => s.Name == "upload")?.Bytes;
            long? bDown = rec.Steps.FirstOrDefault(s => s.Name == "download")?.Bytes;

            var t = rec.Steps.ToDictionary(s => s.Name, s => s.Elapsed.TotalMilliseconds);
            var st = rec.Steps.ToDictionary(s => s.Name, s => s.HttpStatus);
            double totalMs = rec.Steps.Sum(s => s.Elapsed.TotalMilliseconds);

            sb.AppendLine(string.Join(",",
                rec.StartedAt.ToString("o"),
                rec.CorrelationId,
                Csv(rec.InputKey),
                Csv(rec.OutputKey),
                rec.Success,
                Csv(rec.Error),
                t.GetValueOrDefault("presign_upload").ToString(CultureInfo.InvariantCulture),
                t.GetValueOrDefault("upload").ToString(CultureInfo.InvariantCulture),
                t.GetValueOrDefault("invoke").ToString(CultureInfo.InvariantCulture),
                t.GetValueOrDefault("presign_download").ToString(CultureInfo.InvariantCulture),
                t.GetValueOrDefault("download").ToString(CultureInfo.InvariantCulture),
                st.GetValueOrDefault("presign_upload"),
                st.GetValueOrDefault("upload"),
                st.GetValueOrDefault("invoke"),
                st.GetValueOrDefault("presign_download"),
                st.GetValueOrDefault("download"),
                bUp?.ToString() ?? "",
                bDown?.ToString() ?? "",
                totalMs.ToString(CultureInfo.InvariantCulture)
            ));
            File.AppendAllText(file, sb.ToString());
        }
        catch (Exception ex)
        {
            Error("Failed to write run log CSV", ex);
        }   
    }
    private static string Csv(string? s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

    public event Action<string>? Line; // 한 줄 로그를 구독자(MainVM)가 받음
    private static readonly AsyncLocal<string?> _scope = new();

    public IDisposable Scope(string nameOrId)
    {
        var prev = _scope.Value;
        _scope.Value = nameOrId;
        return new ScopeGuard(() => _scope.Value = prev);
    }

    public void Info(string message) => Emit("INFO", message);
    public void Warn(string message) => Emit("WARN", message);
    public void Error(string message, Exception? ex = null)
    {
        if (ex != null) message = $"{message} | {ex.GetType().Name}: {ex.Message}";
        Emit("ERROR", message);
    }

    private void Emit(string level, string msg)
    {
        var s = _scope.Value;
        var prefix = s is null ? "" : $"[{s}] ";
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {prefix}{msg}";
        try { Line?.Invoke(line); } catch { /* swallow */ }
        Debug.WriteLine(line);
    }

    private sealed class ScopeGuard : IDisposable
    {
        private readonly Action _onDispose;
        public ScopeGuard(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }
}

public static class StopwatchExt
{
    public static async Task<(TimeSpan Elapsed, T Result)> TimeAsync<T>(Func<Task<T>> func)
    { var sw = Stopwatch.StartNew(); var res = await func(); sw.Stop(); return (sw.Elapsed, res); }
}
