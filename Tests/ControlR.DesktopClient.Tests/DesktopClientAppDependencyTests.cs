using Avalonia.Controls.ApplicationLifetimes;
using ControlR.DesktopClient.Common.ServiceInterfaces;
using ControlR.DesktopClient.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace ControlR.DesktopClient.Tests;

public class DesktopClientAppDependencyTests
{
  [Theory]
  [InlineData(DesktopEnvironmentType.X11, "Development")]
  [InlineData(DesktopEnvironmentType.Wayland, "Development")]
  [InlineData(DesktopEnvironmentType.X11, "Production")]
  [InlineData(DesktopEnvironmentType.Wayland, "Production")]
  public void Build_ValidatesDependencyGraph(DesktopEnvironmentType desktopEnvironment, string environment)
  {
    switch (desktopEnvironment)
    {
      case DesktopEnvironmentType.X11:
        Environment.SetEnvironmentVariable("DISPLAY", ":0");
        Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);
        break;
      case DesktopEnvironmentType.Wayland:
        Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "wayland-0");
        Environment.SetEnvironmentVariable("DISPLAY", null);
        break;
    }
    Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", environment);
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
      EnvironmentName = environment
    });

    var instanceId = $"test-{Guid.NewGuid()}";
    var mockLifetime = new Mock<IControlledApplicationLifetime>();

    builder.Services.AddSingleton(mockLifetime.Object);
    builder.Services
      .AddDesktopShellServices(instanceId)
      .AddDesktopAppPlatformServices();

    try
    {
      using var host = builder.Build();
      Assert.NotNull(host);
    }
    finally
    {
      Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", null);
    }
  }
}
