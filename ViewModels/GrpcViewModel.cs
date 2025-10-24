using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepDenoiseClient.Services.Common;
using DeepDenoiseClient.Services.Grpc;
using System.Text.Json;


namespace DeepDenoiseClient.ViewModels;

public partial class GrpcViewModel : ObservableObject
{
    private readonly SettingsService _settings;
    private readonly GrpcInvokeService _grpc;

    [ObservableProperty] private string grpcEndpoint = "";
    [ObservableProperty] private string inputKey = "";
    [ObservableProperty] private string outputKey = "";
    [ObservableProperty] private string correlationId = Guid.NewGuid().ToString("N");
    [ObservableProperty]
    private string requestJson =
        "{\n" +
        "  \"model\": \"efficient\",\n" +
        "  \"pixel_pitch\": 140,\n" +
        "  \"type\": \"static\",\n" +
        "  \"strength\": 20,\n" +
        "  \"width\": 3072,\n" +
        "  \"height\": 3072,\n" +
        "  \"using_bits\": 16,\n" +
        "  \"digital_offset\": 100\n" +
        "  \"img_input_url\": \"s3://ddn-in-bucket/user/static_demo_140um_madible_VD-1.tif\"\n" +
        "  \"img_output_url\": \"s3://ddn-out-bucket/user/output_static_demo_140um_madible_VD-1.tif\"\n" +
        "}";
    [ObservableProperty] private string responseText = "";

    public GrpcViewModel(SettingsService settings, GrpcInvokeService grpc)
    { _settings = settings; _grpc = grpc; RefreshFromSettings(); }

    public void RefreshFromSettings() => GrpcEndpoint = _settings.ActiveProfile.GrpcUri.ToString();

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task InvokeAsync(CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(RequestJson);
        var (reply, code) = await _grpc.ProcessAsync(
            InputKey, 
            string.IsNullOrWhiteSpace(OutputKey) ? null : OutputKey, 
            doc, 
            CorrelationId, ct);
        
        ResponseText = $"gRPC Status: {code}\noutput_key: {reply.OutputKey}\noutput_url: {reply.OutputUrl}\nstatus: {reply.Status}";

        CorrelationId = Guid.NewGuid().ToString("N");
    }
}