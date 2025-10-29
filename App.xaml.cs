using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using DeepDenoiseClient.Services.Common;
using DeepDenoiseClient.Services.Alb;
using DeepDenoiseClient.Services.Grpc;
using DeepDenoiseClient.Services.Lambda;
using DeepDenoiseClient.ViewModels;
using DeepDenoiseClient.Views;

namespace DeepDenoiseClient;

public partial class App : Application
{
    public static IHost AppHost { get; private set; } = null!;

    public App()
    {
        AppHost = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // Common
                services.AddSingleton<SettingsService>();
                services.AddSingleton<S3TransferService>();
                services.AddSingleton<RunLogService>();
                services.AddSingleton<IStatusbarService, StatusbarService>();

                // ALB routes
                services.AddSingleton<HealthService>();
                services.AddSingleton<InvokeService>();

                // Lambda routes
                services.AddSingleton<PresignService>();

                // gRPC routes
                services.AddSingleton<GrpcInvokeService>();

                // VMs
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<HttpViewModel>();
                services.AddSingleton<GrpcViewModel>();

                // Views
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await AppHost.StartAsync();
        var window = AppHost.Services.GetRequiredService<MainWindow>();
        window.DataContext = AppHost.Services.GetRequiredService<MainViewModel>();
        window.Show();
        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await AppHost.StopAsync();
        AppHost.Dispose();
        base.OnExit(e);
    }
}
