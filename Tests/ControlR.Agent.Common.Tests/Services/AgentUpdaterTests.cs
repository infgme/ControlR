using ControlR.Agent.Shared.Options;
using ControlR.Agent.Shared.Services;
using ControlR.Agent.Common.Services;
using ControlR.ApiClient;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi;
using ControlR.Libraries.Api.Contracts.Enums;
using ControlR.Libraries.Shared.Primitives;
using ControlR.Libraries.Shared.Services;
using ControlR.Libraries.Shared.Services.FileSystem;
using ControlR.Libraries.Shared.Services.Http;
using ControlR.Libraries.Shared.Services.Processes;
using ControlR.Libraries.TestingUtilities.FileSystem;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using System.Security.Cryptography;

namespace ControlR.Agent.Common.Tests.Services;

public class AgentUpdaterTests
{
  private static readonly Uri _serverUri = new("https://controlr.example/");
  private static readonly Guid _tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

  [Fact]
  public async Task CheckForUpdate_WhenInstalledBundleHashDiffers_DownloadsAndLaunchesInstaller()
  {
    var fixture = new AgentUpdaterFixture();
    fixture.FileSystem.AddFile(fixture.BundleHashPath, "OLD_HASH");

    var installerBytes = new byte[] { 1, 2, 3, 4, 5 };
    var installerSha256 = Convert.ToHexString(SHA256.HashData(installerBytes));
    var downloadedInstallerPath = string.Empty;
    var launchedInstallerPath = string.Empty;
    var launchedInstallerArguments = string.Empty;
    var launchedProcess = new Mock<IProcess>();
    launchedProcess
      .Setup(x => x.WaitForExitAsync(It.IsAny<CancellationToken>()))
      .Returns(Task.CompletedTask);

    fixture.AgentUpdateApi
      .Setup(x => x.GetBundleMetadata(RuntimeId.WinX64, It.IsAny<CancellationToken>()))
      .ReturnsAsync(ApiResult.Ok(new BundleMetadataDto
      {
        BundleDownloadUrl = "/downloads/win-x64/ControlR.Agent.bundle.zip",
        BundleSha256 = "NEW_HASH",
        InstallerDownloadUrl = "/downloads/win-x64/ControlR.Agent.Installer.exe",
        InstallerSha256 = installerSha256,
        Runtime = RuntimeId.WinX64,
        Version = Version.Parse("1.2.3")
      }));

    fixture.AgentUpdateApi
      .Setup(x => x.GetCurrentAgentHashSha256(It.IsAny<RuntimeId>(), It.IsAny<CancellationToken>()))
      .Throws(new InvalidOperationException("Legacy hash endpoint should not be used."));

    fixture.DownloadsApi
      .Setup(x => x.DownloadFile("/downloads/win-x64/ControlR.Agent.Installer.exe", It.IsAny<string>(), It.IsAny<CancellationToken>()))
      .Returns<string, string, CancellationToken>((_, destinationPath, _) =>
      {
        downloadedInstallerPath = destinationPath;
        fixture.FileSystem.AddFile(destinationPath, installerBytes);
        return Task.FromResult(Result.Ok());
      });

    fixture.ProcessManager
      .Setup(x => x.Start(It.IsAny<string>(), It.IsAny<string>()))
      .Returns<string, string>((fileName, arguments) =>
      {
        launchedInstallerPath = fileName;
        launchedInstallerArguments = arguments;
        return launchedProcess.Object;
      });

    var updater = fixture.CreateUpdater();

    await updater.CheckForUpdate(force: true, cancellationToken: TestContext.Current.CancellationToken);

    Assert.EndsWith("ControlR.Agent.Installer.exe", downloadedInstallerPath, StringComparison.OrdinalIgnoreCase);
    Assert.Equal(downloadedInstallerPath, launchedInstallerPath);
    Assert.Contains("install", launchedInstallerArguments, StringComparison.Ordinal);
    Assert.Contains("--server-uri \"https://controlr.example/\"", launchedInstallerArguments, StringComparison.Ordinal);
    Assert.Contains($"--tenant-id {_tenantId}", launchedInstallerArguments, StringComparison.Ordinal);
    Assert.Contains("--instance-id \"instance-1\"", launchedInstallerArguments, StringComparison.Ordinal);
    fixture.AgentUpdateApi.Verify(
      x => x.GetCurrentAgentHashSha256(It.IsAny<RuntimeId>(), It.IsAny<CancellationToken>()),
      Times.Never);
  }

  [Fact]
  public async Task CheckForUpdate_WhenInstalledBundleHashMatches_DoesNotDownloadOrLaunchInstaller()
  {
    var fixture = new AgentUpdaterFixture();
    fixture.FileSystem.AddFile(fixture.BundleHashPath, "ABC123");

    fixture.AgentUpdateApi
      .Setup(x => x.GetBundleMetadata(RuntimeId.WinX64, It.IsAny<CancellationToken>()))
      .ReturnsAsync(ApiResult.Ok(new BundleMetadataDto
      {
        BundleDownloadUrl = "/downloads/win-x64/ControlR.Agent.bundle.zip",
        BundleSha256 = "ABC123",
        InstallerDownloadUrl = "/downloads/win-x64/ControlR.Agent.Installer.exe",
        InstallerSha256 = "DEF456",
        Runtime = RuntimeId.WinX64,
        Version = Version.Parse("1.2.3")
      }));

    fixture.AgentUpdateApi
      .Setup(x => x.GetCurrentAgentHashSha256(It.IsAny<RuntimeId>(), It.IsAny<CancellationToken>()))
      .Throws(new InvalidOperationException("Legacy hash endpoint should not be used."));

    var updater = fixture.CreateUpdater();

    await updater.CheckForUpdate(force: true, cancellationToken: TestContext.Current.CancellationToken);

    fixture.DownloadsApi.Verify(
      x => x.DownloadFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
      Times.Never);
    fixture.ProcessManager.Verify(
      x => x.Start(It.IsAny<string>(), It.IsAny<string>()),
      Times.Never);
    fixture.AgentUpdateApi.Verify(
      x => x.GetBundleMetadata(RuntimeId.WinX64, It.IsAny<CancellationToken>()),
      Times.Once);
    fixture.AgentUpdateApi.Verify(
      x => x.GetCurrentAgentHashSha256(It.IsAny<RuntimeId>(), It.IsAny<CancellationToken>()),
      Times.Never);
  }

  private sealed class AgentUpdaterFixture
  {
    public AgentUpdaterFixture()
    {
      ControlrApi
        .SetupGet(x => x.AgentUpdate)
        .Returns(AgentUpdateApi.Object);

      HostApplicationLifetime
        .SetupGet(x => x.ApplicationStopping)
        .Returns(CancellationToken.None);

      SettingsProvider
        .SetupGet(x => x.DisableAutoUpdate)
        .Returns(false);
      SettingsProvider
        .SetupGet(x => x.ServerUri)
        .Returns(_serverUri);
      SettingsProvider
        .Setup(x => x.GetRequiredTenantId())
        .Returns(_tenantId);

      SystemEnvironment
        .SetupGet(x => x.Runtime)
        .Returns(RuntimeId.WinX64);
      SystemEnvironment
        .SetupGet(x => x.Platform)
        .Returns(SystemPlatform.Windows);

      PathProvider
        .Setup(x => x.GetBundleHashFilePath())
        .Returns(BundleHashPath);
    }

    public Mock<IAgentUpdateApi> AgentUpdateApi { get; } = new();
    public string BundleHashPath { get; } = @"C:\ControlR\.controlr-bundle.sha256";
    public Mock<IControlrApi> ControlrApi { get; } = new();
    public Mock<IDownloadsApi> DownloadsApi { get; } = new();
    public FakeFileSystem FileSystem { get; } = new('\\');
    public Mock<IHostApplicationLifetime> HostApplicationLifetime { get; } = new();
    public Mock<IFileSystemPathProvider> PathProvider { get; } = new();
    public Mock<IProcessManager> ProcessManager { get; } = new();
    public Mock<ISettingsProvider> SettingsProvider { get; } = new();
    public Mock<ISystemEnvironment> SystemEnvironment { get; } = new();

    public AgentUpdater CreateUpdater()
    {
      return new AgentUpdater(
        TimeProvider.System,
        ControlrApi.Object,
        DownloadsApi.Object,
        FileSystem,
        PathProvider.Object,
        ProcessManager.Object,
        SystemEnvironment.Object,
        SettingsProvider.Object,
        HostApplicationLifetime.Object,
        Options.Create(new InstanceOptions { InstanceId = "instance-1" }),
        NullLogger<AgentUpdater>.Instance);
    }

    private sealed class NoopDisposable : IDisposable
    {
      public void Dispose()
      {
      }
    }
  }
}