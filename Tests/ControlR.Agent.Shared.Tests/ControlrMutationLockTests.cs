using ControlR.Agent.Shared.Options;
using ControlR.Agent.Shared.Services;
using ControlR.Libraries.TestingUtilities.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ControlR.Agent.Shared.Tests;

public class ControlrMutationLockTests
{
  [Fact]
  public async Task TryAcquireAsync_WhenInstancesUseDifferentLockScopes_AllowsConcurrentAcquisition()
  {
    var fileSystem = CreateFileSystem();
    var firstLock = CreateSut(fileSystem, CreateAppSettingsPath("instance-1"), "instance-1");
    var secondLock = CreateSut(fileSystem, CreateAppSettingsPath("instance-2"), "instance-2");

    using var firstHandle = await firstLock.TryAcquireAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    using var secondHandle = await secondLock.TryAcquireAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

    Assert.NotNull(firstHandle);
    Assert.NotNull(secondHandle);
  }

  [Fact]
  public async Task TryAcquireAsync_WhenLockIsHeldBySameInstance_TimesOutUntilReleased()
  {
    var mutationLock = CreateSut(CreateFileSystem(), CreateAppSettingsPath("instance-1"), "instance-1");

    using var firstHandle = await mutationLock.TryAcquireAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    var secondHandle = await mutationLock.TryAcquireAsync(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

    Assert.NotNull(firstHandle);
    Assert.Null(secondHandle);

    firstHandle.Dispose();

    using var thirdHandle = await mutationLock.TryAcquireAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

    Assert.NotNull(thirdHandle);
  }

  [Fact]
  public async Task TryAcquireAsync_WhenSameGlobalLockIsHeldByAnotherInstance_TimesOut()
  {
    var appSettingsPath = CreateAppSettingsPath("instance-1");
    var fileSystem = CreateFileSystem();
    var firstLock = CreateSut(fileSystem, appSettingsPath, "instance-1");
    var secondLock = CreateSut(fileSystem, appSettingsPath, "instance-1");

    using var firstHandle = await firstLock.TryAcquireAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    var secondHandle = await Task.Run(() =>
      secondLock.TryAcquireAsync(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken));

    Assert.NotNull(firstHandle);
    Assert.Null(secondHandle);
  }

  private static string CreateAppSettingsPath(string instanceId)
  {
    return OperatingSystem.IsWindows()
      ? $@"C:\ProgramData\ControlR\{instanceId}\appsettings.json"
      : $"/var/lib/controlr/{instanceId}/appsettings.json";
  }

  private static FakeFileSystem CreateFileSystem()
  {
    return OperatingSystem.IsWindows()
      ? new FakeFileSystem('\\')
      : new FakeFileSystem('/');
  }

  private static ControlrMutationLock CreateSut(FakeFileSystem fileSystem, string appSettingsPath, string? instanceId)
  {
    var pathProvider = new Mock<IFileSystemPathProvider>();
    pathProvider
      .Setup(x => x.GetAgentAppSettingsPath())
      .Returns(appSettingsPath);

    return new ControlrMutationLock(
      fileSystem,
      pathProvider.Object,
      Microsoft.Extensions.Options.Options.Create(new InstanceOptions { InstanceId = instanceId }),
      NullLogger<ControlrMutationLock>.Instance);
  }
}