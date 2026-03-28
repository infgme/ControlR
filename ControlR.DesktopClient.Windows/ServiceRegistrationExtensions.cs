using Avalonia.Controls;
using ControlR.DesktopClient.Common.ServiceInterfaces;
using ControlR.DesktopClient.Common.Services;
using ControlR.DesktopClient.Common.Services.Encoders;
using ControlR.DesktopClient.ViewModels;
using ControlR.DesktopClient.Windows.Services;
using ControlR.Libraries.NativeInterop.Windows;
using ControlR.Libraries.Serilog;
using ControlR.Libraries.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ControlR.DesktopClient.Windows;

public static class ServiceRegistrationExtensions
{
  public static IServiceCollection AddDesktopAppPlatformServices(this IServiceCollection services)
  {
    return services
      .AddSharedPlatformServices()
      .AddSingleton<INavigationItemProvider, WindowsNavigationItemProvider>()
      .AddSingleton<IRemoteControlHostBuilderFactory, WindowsRemoteControlHostBuilderFactory>()
      .AddSingleton<IPermissionsViewModel, PermissionsViewModel>();
  }

  public static IHostApplicationBuilder AddRemoteControlPlatformServices(this IHostApplicationBuilder builder)
  {
    builder.Services.AddSharedPlatformServices();
    AddRemoteControlHostedServices(builder.Services);

    return builder;
  }

  public static IServiceCollection AddSharedPlatformServices(this IServiceCollection services)
  {
    return services
      .AddSingleton<IWin32Interop, Win32Interop>()
      .AddSingleton<InputSimulatorWindows>()
      .AddSingleton<IInputSimulator>(provider => provider.GetRequiredService<InputSimulatorWindows>())
      .AddSingleton<ICaptureMetrics, CaptureMetricsWindows>()
      .AddSingleton<IClipboardManager, ClipboardManagerWindows>()
      .AddSingleton<DisplayManagerWindows>()
      .AddSingleton<IDisplayManager>(provider => provider.GetRequiredService<DisplayManagerWindows>())
      .AddSingleton<IDisplayManagerWindows>(provider => provider.GetRequiredService<DisplayManagerWindows>())
      .AddSingleton<IScreenGrabberFactory, ScreenGrabberFactory<ScreenGrabberWindows>>()
      .AddSingleton(provider => provider.GetRequiredService<IScreenGrabberFactory>().GetOrCreateDefault())
      .AddSingleton<IDxOutputDuplicator, DxOutputDuplicator>()
      .AddSingleton<IWindowsMessagePump, WindowsMessagePump>()
      .AddSingleton<IAeroPeekProvider, AeroPeekProvider>();
  }

  private static IServiceCollection AddRemoteControlHostedServices(IServiceCollection services)
  {
    return services
      .AddHostedService(provider => provider.GetRequiredService<IWindowsMessagePump>())
      .AddHostedService<SystemEventHandler>()
      .AddHostedService<RemoteControlEnvironmentService>()
      .AddHostedService<InputDesktopReporter>()
      .AddHostedService(provider => provider.GetRequiredService<InputSimulatorWindows>())
      .AddHostedService<CursorWatcherWindows>();
  }
}