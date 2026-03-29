using ControlR.Libraries.Api.Contracts.Dtos.ServerApi;
using ControlR.Libraries.Shared.Services.FileSystem;
using ControlR.Libraries.Shared.Services.Http;
using ControlR.Libraries.Shared.Services.Processes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace ControlR.Agent.Common.Services;

/// <summary>
/// Responsible for updating the main ControlR agent.
/// </summary>
internal interface IAgentUpdater : IHostedService
{
  /// <summary>
  /// Checks for updates to the ControlR agent.
  /// </summary>
  /// <param name="force">Whether to force an update check, bypassing DisableAutoUpdate in developer settings.</param>
  /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
  /// <returns>A task representing the asynchronous operation.</returns>
  Task CheckForUpdate(bool force = false, CancellationToken cancellationToken = default);
}

internal class AgentUpdater(
  TimeProvider timeProvider,
  IControlrApi controlrApi,
  IDownloadsApi downloadsApi,
  IFileSystem fileSystem,
  IFileSystemPathProvider fileSystemPathProvider,
  IProcessManager proessManager,
  ISystemEnvironment environmentHelper,
  IOptionsAccessor optionsAccessor,
  IHostApplicationLifetime appLifetime,
  IOptions<InstanceOptions> instanceOptions,
  ILogger<AgentUpdater> logger) : BackgroundService, IAgentUpdater
{
  private readonly IHostApplicationLifetime _appLifetime = appLifetime;
  private readonly IControlrApi _controlrApi = controlrApi;
  private readonly IDownloadsApi _downloadsApi = downloadsApi;
  private readonly IFileSystem _fileSystem = fileSystem;
  private readonly IFileSystemPathProvider _fileSystemPathProvider = fileSystemPathProvider;
  private readonly IOptions<InstanceOptions> _instanceOptions = instanceOptions;
  private readonly ILogger<AgentUpdater> _logger = logger;
  private readonly IOptionsAccessor _optionsAccessor = optionsAccessor;
  private readonly IProcessManager _processManager = proessManager;
  private readonly ISystemEnvironment _systemEnvironment = environmentHelper;
  private readonly TimeProvider _timeProvider = timeProvider;

  public async Task CheckForUpdate(bool force = false, CancellationToken cancellationToken = default)
  {
    if (!force && _optionsAccessor.DisableAutoUpdate)
    {
      _logger.LogInformation("Auto-update disabled in developer options.  Skipping update check.");
      return;
    }

    using var logScope = _logger.BeginMemberScope();

    using var updateCts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken,
        _appLifetime.ApplicationStopping,
        updateCts.Token);

    try
    {
      _logger.LogInformation("Beginning version check.");

      var metadataResult = await _controlrApi.AgentUpdate.GetBundleMetadata(_systemEnvironment.Runtime, linkedCts.Token);
      if (!metadataResult.IsSuccess || metadataResult.Value is null)
      {
        _logger.LogErrorDeduped(
          "Failed to retrieve bundle metadata. Reason: {Reason}, StatusCode: {StatusCode}",
          args: [metadataResult.Reason, metadataResult.StatusCode]);
        return;
      }

      var metadata = metadataResult.Value;
      _logger.LogInformation("Remote bundle hash: {RemoteHash}", metadata.BundleSha256);

      var localHash = GetInstalledBundleHash();
      if (!string.IsNullOrWhiteSpace(localHash))
      {
        _logger.LogInformation("Installed bundle hash: {LocalHash}", localHash);
      }

      if (string.Equals(localHash, metadata.BundleSha256, StringComparison.OrdinalIgnoreCase))
      {
        _logger.LogInformation("Version is current (hash match).");
        return;
      }

      _logger.LogInformation("Update found. Downloading bootstrap installer.");

      var installerPath = await DownloadInstaller(metadata, linkedCts.Token);
      if (installerPath is null)
      {
        return;
      }

      _logger.LogInformation("Launching installer.");

      var installCommand = BuildInstallCommand();
      await LaunchInstaller(installerPath, installCommand, linkedCts.Token);
    }
    catch (OperationCanceledException ex)
    {
      _logger.LogInformation(ex, "Timed out during the update check process.");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error while checking for updates.");
    }
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (_optionsAccessor.DisableAutoUpdate)
    {
      _logger.LogInformation("Auto-update disabled in developer options.  Skipping update check timer.");
      return;
    }

    await CheckForUpdate(cancellationToken: stoppingToken);

    using var timer = new PeriodicTimer(TimeSpan.FromHours(6), _timeProvider);

    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
      await CheckForUpdate(cancellationToken: stoppingToken);
    }
  }

  private static string GetInstallerFileName(string downloadPath)
  {
    if (Uri.TryCreate(downloadPath, UriKind.RelativeOrAbsolute, out var uri))
    {
      var sourcePath = uri.IsAbsoluteUri ? uri.LocalPath : uri.OriginalString;
      var fileName = Path.GetFileName(sourcePath);
      if (!string.IsNullOrWhiteSpace(fileName))
      {
        return fileName;
      }
    }

    throw new InvalidOperationException($"Installer download path '{downloadPath}' does not contain a file name.");
  }

  private static string QuoteArgument(string value)
  {
    var escapedValue = value.Replace("\"", "\\\"");
    return $"\"{escapedValue}\"";
  }

  private string BuildInstallCommand()
  {
    var arguments = new List<string>
    {
      "install",
      $"--server-uri {QuoteArgument(_optionsAccessor.ServerUri.ToString())}",
      $"--tenant-id {_optionsAccessor.GetRequiredTenantId()}"
    };

    if (!string.IsNullOrWhiteSpace(_instanceOptions.Value.InstanceId))
    {
      arguments.Add($"--instance-id {QuoteArgument(_instanceOptions.Value.InstanceId)}");
    }

    return string.Join(" ", arguments);
  }

  private async Task<string?> DownloadInstaller(BundleMetadataDto metadata, CancellationToken cancellationToken)
  {
    var tempDirPath = string.IsNullOrWhiteSpace(_instanceOptions.Value.InstanceId)
      ? Path.Combine(Path.GetTempPath(), "ControlR_Update")
      : Path.Combine(Path.GetTempPath(), "ControlR_Update", _instanceOptions.Value.InstanceId);

    _ = _fileSystem.CreateDirectory(tempDirPath);

    var installerFileName = GetInstallerFileName(metadata.InstallerDownloadUrl);
    var installerPath = Path.Combine(tempDirPath, installerFileName);

    if (_fileSystem.FileExists(installerPath))
    {
      _fileSystem.DeleteFile(installerPath);
    }

    var downloadResult = await _downloadsApi.DownloadFile(metadata.InstallerDownloadUrl, installerPath, cancellationToken);
    if (!downloadResult.IsSuccess)
    {
      _logger.LogCritical(
        "Failed to download installer from {DownloadUrl}. Reason: {Reason}",
        metadata.InstallerDownloadUrl,
        downloadResult.Reason);
      return null;
    }

    var installerBytes = await _fileSystem.ReadAllBytesAsync(installerPath);
    var installerHash = Convert.ToHexString(SHA256.HashData(installerBytes));
    if (!string.Equals(installerHash, metadata.InstallerSha256, StringComparison.OrdinalIgnoreCase))
    {
      _logger.LogCritical(
        "Installer hash mismatch. Expected: {ExpectedHash}, Actual: {ActualHash}",
        metadata.InstallerSha256,
        installerHash);
      _fileSystem.DeleteFile(installerPath);
      return null;
    }

    return installerPath;
  }

  private string? GetInstalledBundleHash()
  {
    var bundleHashPath = _fileSystemPathProvider.GetBundleHashFilePath();
    if (!_fileSystem.FileExists(bundleHashPath))
    {
      _logger.LogInformation("Installed bundle hash file was not found at {HashPath}.", bundleHashPath);
      return null;
    }

    var installedHash = _fileSystem.ReadAllText(bundleHashPath).Trim();
    return string.IsNullOrWhiteSpace(installedHash) ? null : installedHash;
  }

  private async Task LaunchInstaller(string installerPath, string installCommand, CancellationToken cancellationToken)
  {
    switch (_systemEnvironment.Platform)
    {
      case SystemPlatform.Windows:
        await _processManager
          .Start(installerPath, installCommand)
          .WaitForExitAsync(cancellationToken);
        break;

      case SystemPlatform.Linux:
        await _processManager
          .Start("sudo", $"chmod +x {installerPath}")
          .WaitForExitAsync(cancellationToken);

        // Use systemd-run to launch installer in a separate scope to prevent
        // it from being killed when the agent service stops
        var systemdRunCommand = $"--scope {installerPath} {installCommand}";
        await _processManager.StartAndWaitForExit(
          "sudo",
          $"systemd-run {systemdRunCommand}",
          false,
          cancellationToken);
        break;

      case SystemPlatform.MacOs:
        await _processManager
          .Start("sudo", $"chmod +x {installerPath}")
          .WaitForExitAsync(cancellationToken);

        await _processManager.StartAndWaitForExit(
          "/bin/zsh",
          $"-c \"{installerPath} {installCommand} &\"",
          true,
          cancellationToken);
        break;

      default:
        throw new PlatformNotSupportedException();
    }
  }
}