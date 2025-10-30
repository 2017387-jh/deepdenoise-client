using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

public partial class ProfileDialogViewModel : ObservableObject
{
    [ObservableProperty] private string profileName;
    [ObservableProperty] private string apiBase;
    [ObservableProperty] private bool? dialogResult;

    public ICommand SaveCommand { get; }

    public ProfileDialogViewModel()
    {
        DialogResult = false;

        SaveCommand = new RelayCommand(OnSave);
    }

    private void OnSave()
    {
        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            MessageBox.Show("Profile Name을 입력하세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(ApiBase))
        {
            MessageBox.Show("API Base URL을 입력하세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!(ApiBase.StartsWith("http://") || ApiBase.StartsWith("https://")))
        {
            MessageBox.Show("API Base URL은 반드시 http:// 또는 https://로 시작해야 합니다.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    partial void OnProfileNameChanged(string value)
    {
        (SaveCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    partial void OnApiBaseChanged(string value)
    {
        (SaveCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }
}