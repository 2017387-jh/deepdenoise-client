using System.Text.Json;
using System.IO;
using DeepDenoiseClient.Models;

namespace DeepDenoiseClient.Services.Common;

public sealed class ProfileInfo
{
    public string Name { get; init; } = "Dev";
    public string ApiBase { get; init; } = "";
    public string? GrpcEndpoint { get; init; }
    public string HealthPath { get; init; } = "/healthz";
    public string InvokePath { get; init; } = "/invocations";
    public string PresignPath { get; init; } = "/presign";

    public string InBucket { get; init; } = "ddn-in-bucket";
    public string OutBucket { get; init; } = "ddn-out-bucket";
    public InvokeRequestModel Defaults { get; init; } = new();

    public Uri HealthUri => new($"{ApiBase.TrimEnd('/')}{HealthPath}");
    public Uri InvokeUri => new($"{ApiBase.TrimEnd('/')}{InvokePath}");
    public Uri PresignUri => new($"{ApiBase.TrimEnd('/')}{PresignPath}");
    public Uri GrpcUri => new((GrpcEndpoint ?? ApiBase).TrimEnd('/')); // 보통 path 없음
}

public sealed class SettingsService
{
    public ProfileInfo ActiveProfile { get; private set; } = new();

    public SettingsService() => Load("Dev");
    public void SetActiveProfile(string name) => Load(name);

    public IReadOnlyList<string> GetProfileNames()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"appsettings.json not found at {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var profiles = doc.RootElement.GetProperty("Profiles");

        var names = new List<string>();
        foreach (var p in profiles.EnumerateObject())
            names.Add(p.Name);

        // 보기 좋게 알파벳 정렬 원하면 정렬
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private void Load(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"appsettings.json not found at {path}");

        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var p = doc.RootElement.GetProperty("Profiles").GetProperty(name);

        ActiveProfile = new ProfileInfo
        {
            Name = name,
            GrpcEndpoint = p.TryGetProperty("GrpcEndpoint", out var ge) ? ge.GetString() : null,
            ApiBase = p.GetProperty("ApiBase").GetString() ?? "",
            HealthPath = p.GetProperty("HealthPath").GetString() ?? "/healthz",
            InvokePath = p.GetProperty("InvokePath").GetString() ?? "/invocations",
            PresignPath = p.GetProperty("PresignPath").GetString() ?? "/presign",
            InBucket = p.TryGetProperty("InBucket", out var ib) ? ib.GetString() ?? "ddn-in-bucket" : "ddn-in-bucket",
            OutBucket = p.TryGetProperty("OutBucket", out var ob) ? ob.GetString() ?? "ddn-out-bucket" : "ddn-out-bucket",
            Defaults = p.TryGetProperty("Defaults", out var d)
                ? JsonSerializer.Deserialize<InvokeRequestModel>(d.GetRawText()) ?? new InvokeRequestModel()
                : new InvokeRequestModel()
        };
    }
}
