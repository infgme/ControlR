using ControlR.Agent.Shared.Options;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi;
using ControlR.Libraries.Shared.Services.FileSystem;
using ControlR.Libraries.Shared.Services.Processes;
using Microsoft.Extensions.Options;

namespace ControlR.Agent.Shared.Services.Base;

internal abstract class AgentInstallerBase(
  IFileSystem fileSystem,
  IBundleExtractor bundleExtractor,
  IFileSystemPathProvider fileSystemPathProvider,
  IControlrApi controlrApi,
  IDeviceInfoProvider deviceDataGenerator,
  IOptionsAccessor optionsAccessor,
  IProcessManager processManager,
  IOptionsMonitor<AgentAppOptions> appOptions,
  ILogger<AgentInstallerBase> logger)
{
  private readonly IControlrApi _controlrApi = controlrApi;
  private readonly IDeviceInfoProvider _deviceDataGenerator = deviceDataGenerator;
  private readonly IFileSystemPathProvider _fileSystemPathProvider = fileSystemPathProvider;
  private readonly IOptionsAccessor _optionsAccessor = optionsAccessor;

  protected IOptionsMonitor<AgentAppOptions> AppOptions { get; } = appOptions;
  protected IFileSystem FileSystem { get; } = fileSystem;
  protected ILogger<AgentInstallerBase> Logger { get; } = logger;
  protected IProcessManager ProcessManager { get; } = processManager;

  protected static string GetAgentPath(string installDirectory, SystemPlatform platform)
  {
    return Path.Combine(installDirectory, AppConstants.GetAgentFileName(platform));
  }

  protected async Task<Result> CreateDeviceOnServer(Guid? installerKeyId, string? installerKeySecret, Guid[]? tagIds)
  {
    if (installerKeyId is null)
    {
      return Result.Ok();
    }

    if (string.IsNullOrWhiteSpace(installerKeySecret))
    {
      return Result.Fail("Installer key secret is required when installer key ID is provided.");
    }

    tagIds ??= [];

    var deviceDto = await _deviceDataGenerator.GetDeviceInfo();
    var createRequest = new CreateDeviceRequestDto(deviceDto, installerKeyId.Value, installerKeySecret, tagIds);

    Logger.LogInformation("Requesting device creation on the server with tags {TagIds}.", string.Join(", ", tagIds));
    var createResult = await _controlrApi.Devices.CreateDevice(createRequest);
    if (createResult.IsSuccess)
    {
      Logger.LogInformation("Device created successfully.");
    }
    else
    {
      Logger.LogError("Device creation failed.  Reason: {Reason}", createResult.Reason);
    }

    return createResult.ToResult();
  }

  protected Task ExtractBundleToInstallDirectory(
    string bundleZipPath,
    string installDirectory,
    CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(bundleZipPath))
    {
      throw new ArgumentException("Bundle zip path is required.", nameof(bundleZipPath));
    }

    if (!FileSystem.FileExists(bundleZipPath))
    {
      throw new FileNotFoundException($"Bundle zip '{bundleZipPath}' does not exist.", bundleZipPath);
    }

    return bundleExtractor.ExtractBundle(bundleZipPath, installDirectory, cancellationToken);
  }

  protected Result StopProcesses(string targetAgentPath)
  {
    try
    {
      var procs = ProcessManager
        .GetProcessesByName("ControlR.Agent")
        .Where(x => x.Id != Environment.ProcessId && x.FilePath == targetAgentPath);

      foreach (var proc in procs)
      {
        try
        {
          proc.Kill();
        }
        catch (Exception ex)
        {
          Logger.LogError(ex, "Failed to kill agent process with ID {AgentProcessId}.", proc.Id);
        }
      }

      procs = ProcessManager.GetProcessesByName("ControlR.DesktopClient");

      foreach (var proc in procs)
      {
        try
        {
          proc.Kill();
        }
        catch (Exception ex)
        {
          Logger.LogError(ex, "Failed to kill desktop client process with ID {DesktopClientProcessId}.", proc.Id);
        }
      }

      return Result.Ok();
    }
    catch (Exception ex)
    {
      Logger.LogError(ex, "Error while stopping service and processes.");
      return Result.Fail(ex);
    }
  }


  protected async Task UpdateAppSettings(Uri? serverUri, Guid? tenantId, Guid? deviceId)
  {
    using var _ = Logger.BeginMemberScope();
    var currentOptions = AppOptions.CurrentValue;

    var updatedServerUri =
      serverUri ??
      currentOptions.ServerUri ??
      AppConstants.ServerUri;

    var updatedTenantId =
      tenantId ??
      currentOptions.TenantId;

    var updatedDeviceId =
      deviceId ??
      currentOptions.DeviceId;

    Logger.LogInformation("Setting server URI to {ServerUri}.", updatedServerUri);
    currentOptions.ServerUri = updatedServerUri;

    Logger.LogInformation("Setting tenant ID to {TenantId}.", updatedTenantId);
    currentOptions.TenantId = updatedTenantId;

    if (updatedDeviceId == Guid.Empty)
    {
      Logger.LogInformation("DeviceId is empty.  Generating new one.");
      currentOptions.DeviceId = Guid.NewGuid();
    }
    else
    {
      Logger.LogInformation("Setting device ID to {DeviceId}.", updatedDeviceId);
      currentOptions.DeviceId = updatedDeviceId;
    }

    Logger.LogInformation("Writing results to disk.");
    await _optionsAccessor.UpdateAppOptions(currentOptions);
  }

  protected async Task WriteBundleHashFile(string? bundleSha256)
  {
    if (string.IsNullOrWhiteSpace(bundleSha256))
    {
      return;
    }

    var bundleHashPath = _fileSystemPathProvider.GetBundleHashFilePath();
    var settingsDirectory = Path.GetDirectoryName(bundleHashPath)
      ?? throw new DirectoryNotFoundException("Unable to determine the bundle hash directory.");

    Logger.LogInformation("Writing bundle hash to {BundleHashPath}.", bundleHashPath);
    FileSystem.CreateDirectory(settingsDirectory);
    await FileSystem.WriteAllTextAsync(bundleHashPath, bundleSha256.Trim());
  }

}
