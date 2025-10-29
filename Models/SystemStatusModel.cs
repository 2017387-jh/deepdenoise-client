using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DeepDenoiseClient.Models;

// 소스 제너레이터가 INotifyPropertyChanged 구현을 자동 생성
public sealed partial class SystemStatusModel : ObservableObject
{
    // "Success" | "Fail" | ""
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string? _message;
    [ObservableProperty] private TimeSpan _elapsed = TimeSpan.Zero;
}