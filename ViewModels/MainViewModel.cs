using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDenoiseClient.Services.Alb;
using DeepDenoiseClient.Services.Common;
using Grpc.Net.Client.Balancer;
using System.Collections.ObjectModel;

namespace DeepDenoiseClient.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly HealthService _health;

    public HttpViewModel HttpVM { get; }

    public ObservableCollection<string> Profiles { get; } = new(["Dev"]);
    [ObservableProperty] private string selectedProfile = "Dev";
    [ObservableProperty] private string pingResult = "N/A";
    public ObservableCollection<string> Logs { get; } = new();

    public MainViewModel(SettingsService settings, HealthService health, HttpViewModel httpVM)
    { _settings = settings; _health = health; HttpVM = httpVM; }

    [RelayCommand]
    private void ApplyProfile()
    {
        _settings.SetActiveProfile(SelectedProfile);
        HttpVM.RefreshFromSettings();
        Logs.Add($"Profile switched to {SelectedProfile}");
    }

    [RelayCommand]
    private async Task PingAsync()
    {
        var ok = await _health.CheckAsync();
        PingResult = ok ? "OK" : "FAIL";
        Logs.Add($"Ping {PingResult} {_settings.Active.HealthUri}");
    }
}
