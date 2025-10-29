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
├─ SampleImages
│  ├─ 2560x2048.tif
│  ├─ 2560x3072.tif
│  ├─ 3072x3072.tif
│  └─ 4316x4316.tif
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
├─ Utils
│  ├─ Converters
│  │  └─ NullToVisibilityConverter.cs
│  └─ PathUtil.cs
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