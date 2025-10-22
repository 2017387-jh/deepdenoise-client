using System.Text.Json;
using System.IO;

namespace DeepDenoiseClient.Services.Common;

public sealed class ApiRoute
{
    public string ApiBase { get; init; } = "";
    public string? GrpcEndpoint { get; init; }
    public string HealthPath { get; init; } = "/healthz";
    public string InvokePath { get; init; } = "/invocations";
    public string PresignPath { get; init; } = "/presign";

    public Uri HealthUri => new($"{ApiBase.TrimEnd('/')}{HealthPath}");
    public Uri InvokeUri => new($"{ApiBase.TrimEnd('/')}{InvokePath}");
    public Uri PresignUri => new($"{ApiBase.TrimEnd('/')}{PresignPath}");
    public Uri GrpcUri => new((GrpcEndpoint ?? ApiBase).TrimEnd('/')); // 보통 path 없음
}

public sealed class SettingsService
{
    public ApiRoute Active { get; private set; } = new();

    public SettingsService() => Load("Dev");
    public void SetActiveProfile(string name) => Load(name);

    private void Load(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"appsettings.json not found at {path}");

        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var p = doc.RootElement.GetProperty("Profiles").GetProperty(name);

        Active = new ApiRoute
        {
            GrpcEndpoint = p.TryGetProperty("GrpcEndpoint", out var ge) ? ge.GetString() : null,
            ApiBase = p.GetProperty("ApiBase").GetString() ?? "",
            HealthPath = p.GetProperty("HealthPath").GetString() ?? "/healthz",
            InvokePath = p.GetProperty("InvokePath").GetString() ?? "/invocations",
            PresignPath = p.GetProperty("PresignPath").GetString() ?? "/presign"
        };
    }
}
