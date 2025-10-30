using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDenoiseClient.Models;
using DeepDenoiseClient.Services.Alb;
using DeepDenoiseClient.Services.Common;
using Grpc.Net.Client.Balancer;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace DeepDenoiseClient.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly HealthService _health;
    private readonly RunLogService _log;
    private readonly IStatusbarService _statusbar;

    public HttpViewModel HttpVM { get; }

    public SystemStatusModel Status => _statusbar.Current;

    public ObservableCollection<string> Profiles { get; } = new(["Dev"]);
    [ObservableProperty] private string selectedProfile = "Dev";
    [ObservableProperty] private string pingResult = "N/A";
    public ObservableCollection<string> Logs { get; } = new();
    public ObservableCollection<LogModel> LogEntries { get; } = new();


    public MainViewModel(
        SettingsService settings, 
        HealthService health, 
        HttpViewModel httpVM, 
        RunLogService log,
        IStatusbarService statusbar)
    {
        _settings = settings;
        _health = health;
        HttpVM = httpVM;
        _log = log;
        _statusbar = statusbar;

        _log.Entry += entry =>
        {
            App.Current.Dispatcher.Invoke(() => LogEntries.Add(entry));
        };

        _log.Line += s => Application.Current.Dispatcher.Invoke(() => Logs.Add(s));

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

    [RelayCommand]
    private void ClearLogs()
    {
        LogEntries.Clear();
    }

    [RelayCommand]
    private async Task PingAsync()
    {
        using (_statusbar.Begin())
        {
            var (t, ok) = await StopwatchExt.TimeAsync(async () => await _health.CheckAsync());
            PingResult = ok ? "OK" : "FAIL";
            _log.Info($"Ping {PingResult} {_settings.ActiveProfile.HealthUri}", path: $"/healthz", elapsed: t);

            if (ok)
            {
                _statusbar.Success("Ping successful.");
            }
            else
            {
                _statusbar.Fail("Ping failed.");
            }
        }
    }
}
