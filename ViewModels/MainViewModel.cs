using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDenoiseClient.Services.Alb;
using DeepDenoiseClient.Services.Common;
using Grpc.Net.Client.Balancer;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace DeepDenoiseClient.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly HealthService _health;
    private readonly RunLogService _log;

    public HttpViewModel HttpVM { get; }

    public ObservableCollection<string> Profiles { get; } = new(["Dev"]);
    [ObservableProperty] private string selectedProfile = "Dev";
    [ObservableProperty] private string pingResult = "N/A";
    public ObservableCollection<string> Logs { get; } = new();

    public MainViewModel(SettingsService settings, HealthService health, HttpViewModel httpVM, RunLogService log)
    {
        _settings = settings; _health = health; HttpVM = httpVM; _log = log;
        _log.Line += s => Application.Current.Dispatcher.Invoke(() => Logs.Add(s));

        // 추가: appsettings의 모든 프로필 이름으로 채우기
        Profiles.Clear();
        foreach (var name in _settings.GetProfileNames())
            Profiles.Add(name);

        // 추가: 초기 선택값을 존재하면 유지, 없으면 첫 번째로
        if (!Profiles.Contains(SelectedProfile) && Profiles.Count > 0)
            SelectedProfile = Profiles[0];
    }

    partial void OnSelectedProfileChanged(string value)
    {
        _settings.SetActiveProfile(value);
        HttpVM.RefreshFromSettings();

        _log.Info($"Profile switched to {value}");
        _log.Info($"API Base {_settings.ActiveProfile.InvokeUri}");
    }


    //[RelayCommand]
    //private void ApplyProfile()
    //{
    //    _settings.SetActiveProfile(SelectedProfile);
    //    HttpVM.RefreshFromSettings();
    //    Logs.Add($"Profile switched to {SelectedProfile}");
    //}

    [RelayCommand]
    private async Task PingAsync()
    {
        var ok = await _health.CheckAsync();
        PingResult = ok ? "OK" : "FAIL";
        _log.Info($"Ping {PingResult} {_settings.ActiveProfile.HealthUri}");
    }
}
