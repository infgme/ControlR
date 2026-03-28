using ControlR.Agent.Shared.Constants;
using ControlR.Agent.Shared.Models;
using ControlR.Agent.Shared.Options;
using ControlR.Libraries.NativeInterop.Unix;
using ControlR.Libraries.Shared.Services.FileSystem;
using ControlR.Libraries.Shared.Services.Processes;
using Microsoft.Extensions.Options;

namespace ControlR.Agent.Shared.Services.Mac;

internal class AgentInstallerMac(
  IFileSystem fileSystem,
  IFileSystemPathProvider fileSystemPathProvider,
  IServiceControl serviceControl,
  IRetryer retryer,
  IControlrApi controlrApi,
  IEmbeddedResourceAccessor embeddedResourceAccessor,
  IDeviceInfoProvider deviceDataGenerator,
  ISettingsProvider settingsProvider,
  IProcessManager processManager,
  IBundleExtractor bundleExtractor,
  IOptionsMonitor<AgentAppOptions> appOptions,
  IOptions<InstanceOptions> instanceOptions,
  ILogger<AgentInstallerMac> logger)
  : AgentInstallerBase(fileSystem, bundleExtractor, fileSystemPathProvider, controlrApi, deviceDataGenerator, settingsProvider, processManager, appOptions, logger), IAgentInstaller
{
  private const string MacAgentInstallDirectory = "/Library/Application Support/ControlR";
  private const string MacAppBundleName = "ControlR.app";

  private static readonly SemaphoreSlim _installLock = new(1, 1);

  private readonly IEmbeddedResourceAccessor _embeddedResourceAccessor = embeddedResourceAccessor;
  private readonly IFileSystem _fileSystem = fileSystem;
  private readonly IOptions<InstanceOptions> _instanceOptions = instanceOptions;
  private readonly ILogger<AgentInstallerMac> _logger = logger;
  private readonly IServiceControl _serviceControl = serviceControl;

  public async Task Install(AgentInstallRequest request)
  {
    if (!await _installLock.WaitAsync(0))
    {
      _logger.LogWarning("Installer lock already acquired.  Aborting.");
      return;
    }

    try
    {
      _logger.LogInformation("Install started.");

      if (Libc.Geteuid() != 0)
      {
        _logger.LogError("Install command must be run with sudo.");
        return;
      }

      TryClearDotnetExtractDir("/var/root/.net/ControlR.Agent");

      var appBundleInstallPath = GetInstalledAppBundlePath();
      var extractedAppBundlePath = GetExtractedAppBundlePath();
      var installedAgentPath = GetInstalledAgentPath();
      var installedAgentDirectory = Path.GetDirectoryName(installedAgentPath)
        ?? throw new DirectoryNotFoundException("Unable to determine the agent install directory.");
      var bundleStateDirectory = GetBundleStateDirectory();

      await _serviceControl.StopDesktopClientService(throwOnFailure: false);
      await _serviceControl.StopAgentService(throwOnFailure: false);

      if (_fileSystem.DirectoryExists(appBundleInstallPath))
      {
        _fileSystem.DeleteDirectory(appBundleInstallPath, true);
      }

      if (_fileSystem.FileExists(installedAgentPath))
      {
        _fileSystem.DeleteFile(installedAgentPath);
      }

      _fileSystem.CreateDirectory(appBundleInstallPath);
      _fileSystem.CreateDirectory(installedAgentDirectory);
      _fileSystem.CreateDirectory(bundleStateDirectory);

      _logger.LogInformation("Extracting bundle {BundleZipPath} to {InstallDirectory}.", request.BundleZipPath, PathConstants.MacApplicationsDirectory);
      await retryer.Retry(
        () => ExtractBundleToInstallDirectory(request.BundleZipPath, PathConstants.MacApplicationsDirectory),
        tryCount: 5,
        retryDelay: TimeSpan.FromSeconds(1));

      if (!string.Equals(extractedAppBundlePath, appBundleInstallPath, StringComparison.Ordinal))
      {
        if (_fileSystem.DirectoryExists(appBundleInstallPath))
        {
          _fileSystem.DeleteDirectory(appBundleInstallPath, true);
        }

        Directory.Move(extractedAppBundlePath, appBundleInstallPath);
      }

      var sourceAgentPath = GetSourceAgentPath(appBundleInstallPath);

      _logger.LogInformation("Installing agent executable to {AgentPath}.", installedAgentPath);
      _fileSystem.CopyFile(sourceAgentPath, installedAgentPath, overwrite: true);
      SetAgentPermissions(installedAgentPath);

      var agentPlistPath = GetLaunchDaemonFilePath();
      var agentPlistFile = (await GetLaunchDaemonFile()).Trim();
      var desktopPlistPath = GetLaunchAgentFilePath();
      var desktopPlistFile = (await GetLaunchAgentFile()).Trim();

      _logger.LogInformation("Writing plist files.");
      await WriteFileIfChanged(agentPlistPath, agentPlistFile);
      await WriteFileIfChanged(desktopPlistPath, desktopPlistFile);
      await UpdateAppSettings(request.ServerUri, request.TenantId, request.DeviceId);

      var createResult = await CreateDeviceOnServer(request.InstallerKeyId, request.InstallerKeySecret, request.TagIds);
      if (!createResult.IsSuccess)
      {
        return;
      }

      await WriteBundleHashFile(request.BundleSha256);

      await _serviceControl.StartAgentService(throwOnFailure: false);
      await _serviceControl.StartDesktopClientService(throwOnFailure: false);

      _logger.LogInformation("Installer finished.");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error while installing the ControlR service.");
    }
    finally
    {
      _installLock.Release();
    }
  }

  public async Task Uninstall()
  {
    if (!await _installLock.WaitAsync(0))
    {
      _logger.LogWarning("Installer lock already acquired.  Aborting.");
      return;
    }

    try
    {
      _logger.LogInformation("Uninstall started.");

      if (Libc.Geteuid() != 0)
      {
        _logger.LogError("Uninstall command must be run with sudo.");
      }

      var serviceFilePath = GetLaunchDaemonFilePath();
      var desktopFilePath = GetLaunchAgentFilePath();

      _logger.LogInformation("Booting out services.");
      await _serviceControl.StopDesktopClientService(throwOnFailure: false);
      await _serviceControl.StopAgentService(throwOnFailure: false);

      if (_fileSystem.FileExists(serviceFilePath))
      {
        _fileSystem.DeleteFile(serviceFilePath);
      }

      if (_fileSystem.FileExists(desktopFilePath))
      {
        _fileSystem.DeleteFile(desktopFilePath);
      }

      var appBundleInstallPath = GetInstalledAppBundlePath();
      if (_fileSystem.DirectoryExists(appBundleInstallPath))
      {
        _fileSystem.DeleteDirectory(appBundleInstallPath, true);
      }

      var installedAgentPath = GetInstalledAgentPath();
      if (_fileSystem.FileExists(installedAgentPath))
      {
        _fileSystem.DeleteFile(installedAgentPath);
      }

      var bundleStateDirectory = GetBundleStateDirectory();
      if (_fileSystem.DirectoryExists(bundleStateDirectory))
      {
        _fileSystem.DeleteDirectory(bundleStateDirectory, true);
      }

      _logger.LogInformation("Uninstall completed.");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error while uninstalling the ControlR service.");
    }
    finally
    {
      _installLock.Release();
    }
  }

  private string GetAgentServiceName()
  {
    return string.IsNullOrWhiteSpace(_instanceOptions.Value.InstanceId)
      ? "app.controlr.agent"
      : $"app.controlr.agent.{_instanceOptions.Value.InstanceId}";
  }

  private string GetBundleStateDirectory()
  {
    return PathConstants.GetMacBundleStateDirectory(_instanceOptions.Value.InstanceId);
  }

  private string GetDesktopExecutablePath()
  {
    return _fileSystem.JoinPaths('/', GetInstalledAppBundlePath(), PathConstants.MacDesktopExecutableRelativePath);
  }

  private string GetExtractedAppBundlePath()
  {
    return $"{PathConstants.MacApplicationsDirectory}/{MacAppBundleName}";
  }

  private string GetInstalledAgentPath()
  {
    var installDirectory = string.IsNullOrWhiteSpace(_instanceOptions.Value.InstanceId)
      ? MacAgentInstallDirectory
      : $"{MacAgentInstallDirectory}/{_instanceOptions.Value.InstanceId}";

    return _fileSystem.JoinPaths('/', installDirectory, AppConstants.GetAgentFileName(SystemPlatform.MacOs));
  }

  private string GetInstalledAppBundlePath()
  {
    return PathConstants.GetMacInstalledAppPath(_instanceOptions.Value.InstanceId);
  }

  private async Task<string> GetLaunchAgentFile()
  {
    var serviceName = string.IsNullOrWhiteSpace(_instanceOptions.Value.InstanceId)
      ? "app.controlr.desktop"
      : $"app.controlr.desktop.{_instanceOptions.Value.InstanceId}";

    var template = await _embeddedResourceAccessor.GetResourceAsString(
      typeof(AgentInstallerMac).Assembly,
      "LaunchAgent.plist");

    template = template
      .Replace("{{SERVICE_NAME}}", serviceName)
      .Replace("{{DESKTOP_EXECUTABLE_PATH}}", GetDesktopExecutablePath());

    if (string.IsNullOrWhiteSpace(_instanceOptions.Value.InstanceId))
    {
      // Remove lines containing {{INSTANCE_ID}} and <string>--instance-id</string>
      var lines = template.Split('\n');
      lines = [.. lines.Where(line =>
        !line.Contains("{{INSTANCE_ID}}") &&
        !line.Contains("<string>--instance-id</string>"))];

      template = string.Join("\n", lines);
    }
    else
    {
      template = template.Replace("{{INSTANCE_ID}}", _instanceOptions.Value.InstanceId);
    }
    return template;
  }

  private string GetLaunchAgentFilePath()
  {
    if (string.IsNullOrWhiteSpace(_instanceOptions.Value.InstanceId))
    {
      return "/Library/LaunchAgents/app.controlr.desktop.plist";
    }

    return $"/Library/LaunchAgents/app.controlr.desktop.{_instanceOptions.Value.InstanceId}.plist";
  }

  private async Task<string> GetLaunchDaemonFile()
  {
    var template = await _embeddedResourceAccessor.GetResourceAsString(
      typeof(AgentInstallerMac).Assembly,
      "LaunchDaemon.plist");

    template = template
      .Replace("{{SERVICE_NAME}}", GetAgentServiceName())
      .Replace("{{AGENT_PATH}}", GetInstalledAgentPath());

    if (string.IsNullOrWhiteSpace(_instanceOptions.Value.InstanceId))
    {
      // Remove lines containing {{INSTANCE_ID}} and <string>-i</string>
      var lines = template.Split('\n');
      lines = [.. lines.Where(line =>
        !line.Contains("{{INSTANCE_ID}}") &&
        !line.Contains("<string>-i</string>"))];

      template = string.Join("\n", lines);
    }
    else
    {
      template = template.Replace("{{INSTANCE_ID}}", _instanceOptions.Value.InstanceId);
    }
    return template;
  }

  private string GetLaunchDaemonFilePath()
  {
    if (string.IsNullOrWhiteSpace(_instanceOptions.Value.InstanceId))
    {
      return "/Library/LaunchDaemons/app.controlr.agent.plist";
    }

    return $"/Library/LaunchDaemons/app.controlr.agent.{_instanceOptions.Value.InstanceId}.plist";
  }

  private string GetSourceAgentPath(string sourceAppBundlePath)
  {
    var sourceAgentPath = _fileSystem.JoinPaths('/', sourceAppBundlePath, "Contents/Library/LaunchServices", AppConstants.GetAgentFileName(SystemPlatform.MacOs));
    if (!_fileSystem.FileExists(sourceAgentPath))
    {
      throw new FileNotFoundException("The extracted app bundle does not contain the agent executable.", sourceAgentPath);
    }

    return sourceAgentPath;
  }

  private void SetAgentPermissions(string installedAgentPath)
  {
    if (!OperatingSystem.IsMacOS())
    {
      throw new PlatformNotSupportedException();
    }

    _fileSystem.SetUnixFileMode(
      installedAgentPath,
      UnixFileMode.UserRead |
      UnixFileMode.UserWrite |
      UnixFileMode.UserExecute |
      UnixFileMode.GroupRead |
      UnixFileMode.GroupExecute |
      UnixFileMode.OtherRead |
      UnixFileMode.OtherExecute);
  }

  private async Task WriteFileIfChanged(string filePath, string content)
  {
    if (_fileSystem.FileExists(filePath))
    {
      var existingContent = await _fileSystem.ReadAllTextAsync(filePath);
      if (existingContent.Trim() == content)
      {
        _logger.LogInformation("File {FilePath} already exists with the same content. Skipping write.", filePath);
        return;
      }
    }

    _logger.LogInformation("Writing file {FilePath}.", filePath);
    await _fileSystem.WriteAllTextAsync(filePath, content);
  }
}
