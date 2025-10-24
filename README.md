# deepdenoise-client
test for deep denoising service

```
DeepDenoiseClient
├─ App.xaml
├─ App.xaml.cs
├─ appsettings.json
├─ AssemblyInfo.cs
├─ DeepDenoiseClient.csproj
├─ DeepDenoiseClient.sln
├─ Models
│  ├─ PresignModels.cs
│  └─ RequestModel.cs
├─ Protos
│  └─ deepdenoise.proto
├─ README.md
├─ Services
│  ├─ Alb
│  │  ├─ HealthService.cs
│  │  └─ InvokeService.cs
│  ├─ Common
│  │  ├─ ProgressStream.cs
│  │  ├─ RunLogService.cs
│  │  ├─ S3TransferService.cs
│  │  └─ SettingsService.cs
│  ├─ gRPC
│  │  └─ GrpcInvokeService.cs
│  └─ Lambda
│     └─ PresignService.cs
├─ Themes
│  └─ Styles.xaml
├─ ViewModels
│  ├─ GrpcViewModel.cs
│  ├─ HttpViewModel.cs
│  └─ MainViewModel.cs
└─ Views
   ├─ MainWindow.xaml
   ├─ MainWindow.xaml.cs
   └─ Tabs
      ├─ GrpcTab.xaml
      ├─ GrpcTab.xaml.cs
      ├─ HttpTab.xaml
      └─ HttpTab.xaml.cs

```