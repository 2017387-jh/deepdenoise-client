using CommunityToolkit.Mvvm.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace DeepDenoiseClient.Models;

public partial class RequestModel : ObservableValidator
{
    [ObservableProperty, Range(1, 16384)] private int width = 3072;
    [ObservableProperty, Range(1, 16384)] private int height = 3072;
    [ObservableProperty, Range(8, 16)] private int usingBits = 16;

    [ObservableProperty] private string model = "efficient";
    [ObservableProperty] private int pixelPitch = 140;
    [ObservableProperty] private int strength = 20;
    [ObservableProperty] private string type = "static";

    public void ValidateAll() => ValidateAllProperties();
}
