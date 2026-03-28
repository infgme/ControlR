using ControlR.Libraries.Api.Contracts.Dtos.ServerApi;
using ControlR.Libraries.Shared.Services.FileSystem;
using ControlR.Libraries.Shared.Services.Processes;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace ControlR.Agent.Common.Services;

/// <summary>
/// Bridges legacy agent installations to the new bootstrap installer.
/// When an old agent detects it's running the legacy binary, it downloads the new installer
/// and delegates to it, allowing seamless migration to the new bundle-based architecture.
/// </summary>
internal interface ILegacyInstallerBridge
{
  /// <summary>
  /// Downloads the bootstrap installer for the current runtime and forwards install arguments to it.
  /// Returns true if forwarding was successful, false if forwarding should be skipped.
  /// </summary>
  Task<bool> TryForwardToNewInstaller(
    Uri serverUri,
    Guid? tenantId,
    string? installerKeySecret,
    Guid? installerKeyId,
    Guid? deviceId,
    Guid[]? tags,
    string? instanceId,
    CancellationToken cancellationToken = default);
}

internal class LegacyInstallerBridge(
  IProcessManager processManager,
  ISystemEnvironment systemEnvironment,
  IHttpClientFactory httpClientFactory,
  IFileSystem fileSystem,
  ILogger<LegacyInstallerBridge> logger) : ILegacyInstallerBridge
{
  private readonly IFileSystem _fileSystem = fileSystem;
  private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
  private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
  {
    PropertyNameCaseInsensitive = true
  };
  private readonly ILogger<LegacyInstallerBridge> _logger = logger;
  private readonly IProcessManager _processManager = processManager;
  private readonly ISystemEnvironment _systemEnvironment = systemEnvironment;

  /// <summary>
  /// Attempts to forward installation to the new bootstrap installer.
  /// </summary>
  public async Task<bool> TryForwardToNewInstaller(
    Uri serverUri,
    Guid? tenantId,
    string? installerKeySecret,
    Guid? installerKeyId,
    Guid? deviceId,
    Guid[]? tags,
    string? instanceId,
    CancellationToken cancellationToken = default)
  {
    try
    {
      _logger.LogInformation("Legacy installer started. Attempting to forward to new installer.");

      // Determine runtime
      var runtime = _systemEnvironment.Runtime;
      _logger.LogInformation("Detected runtime: {Runtime}", runtime);

      // Fetch bundle metadata
      var metadata = await FetchBundleMetadata(serverUri, runtime, cancellationToken);
      if (metadata == null)
      {
        _logger.LogWarning("Failed to fetch bundle metadata. Skipping forwarding.");
        return false;
      }

      // Download installer
      var installerPath = await DownloadInstaller(
        serverUri,
        metadata,
        Path.GetTempPath(),
        cancellationToken);

      if (installerPath == null)
      {
        _logger.LogWarning("Failed to download installer. Skipping forwarding.");
        return false;
      }

      // Build installer command line arguments
      var args = BuildInstallerArguments(serverUri, tenantId, installerKeySecret, installerKeyId, deviceId, tags, instanceId);

      await CleanupLegacyDesktopService(instanceId, cancellationToken);

      // Launch installer and wait for completion
      _logger.LogInformation("Launching installer: {InstallerPath}", installerPath);
      var process = await LaunchInstaller(installerPath, args, cancellationToken);
      if (process is null)
      {
        _logger.LogError("Failed to launch installer process.");
        return false;
      }

      await process.WaitForExitAsync(cancellationToken);

      if (process.ExitCode == 0)
      {
        _logger.LogInformation("Installer completed successfully");
        return true;
      }
      else
      {
        _logger.LogError("Installer failed with exit code {ExitCode}", process.ExitCode);
        return false;
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error forwarding to new installer");
      return false;
    }
  }

  private static string QuoteArgument(string value)
  {
    var escapedValue = value.Replace("\"", "\\\"");
    return $"\"{escapedValue}\"";
  }

  private string BuildInstallerArguments(
    Uri serverUri,
    Guid? tenantId,
    string? installerKeySecret,
    Guid? installerKeyId,
    Guid? deviceId,
    Guid[]? tags,
    string? instanceId)
  {
    var args = new List<string>
    {
      "install",
      $"--server-uri {QuoteArgument(serverUri.ToString())}"
    };

    if (tenantId.HasValue)
    {
      args.Add($"--tenant-id {tenantId}");
    }

    if (!string.IsNullOrWhiteSpace(instanceId))
    {
      args.Add($"--instance-id {QuoteArgument(instanceId)}");
    }

    if (!string.IsNullOrWhiteSpace(installerKeySecret))
    {
      args.Add($"--installer-key-secret {QuoteArgument(installerKeySecret)}");
    }

    if (installerKeyId.HasValue)
    {
      args.Add($"--installer-key-id {installerKeyId}");
    }

    if (deviceId.HasValue)
    {
      args.Add($"--device-id {deviceId}");
    }

    if (tags is { Length: > 0 })
    {
      args.Add($"--device-tags {QuoteArgument(string.Join(',', tags))}");
    }

    return string.Join(" ", args);
  }

  private async Task CleanupLegacyDesktopService(string? instanceId, CancellationToken cancellationToken)
  {
    if (_systemEnvironment.IsMacOS())
    {
      await CleanupLegacyMacLaunchAgent(instanceId, cancellationToken);
      return;
    }
  }

  private async Task CleanupLegacyMacLaunchAgent(string? instanceId, CancellationToken cancellationToken)
  {
    var serviceName = string.IsNullOrWhiteSpace(instanceId)
      ? "app.controlr.desktop"
      : $"app.controlr.desktop.{instanceId}";

    var plistPath = string.IsNullOrWhiteSpace(instanceId)
      ? "/Library/LaunchAgents/app.controlr.desktop.plist"
      : $"/Library/LaunchAgents/app.controlr.desktop.{instanceId}.plist";

    _logger.LogInformation("Cleaning up legacy macOS LaunchAgent {ServiceName}.", serviceName);

    var users = await GetLoggedInUsers(cancellationToken);
    foreach (var (_, uid) in users)
    {
      await TryRunCommand("launchctl", $"bootout gui/{uid} {QuoteArgument(plistPath)}", cancellationToken);
      await TryRunCommand("launchctl", $"remove {QuoteArgument(serviceName)}", cancellationToken);
    }

    if (File.Exists(plistPath))
    {
      _logger.LogInformation("Removing legacy macOS LaunchAgent plist {PlistPath}.", plistPath);
      File.Delete(plistPath);
    }
  }

  private async Task<string?> DownloadInstaller(
    Uri serverUri,
    BundleMetadataDto metadata,
    string tempPath,
    CancellationToken cancellationToken)
  {
    try
    {
      var installerUrl = new Uri(serverUri, metadata.InstallerDownloadUrl);
      var installerFileName = Path.GetFileName(metadata.InstallerDownloadUrl);
      var installerPath = Path.Combine(tempPath, installerFileName);

      _logger.LogInformation("Downloading installer to {Path}", installerPath);

      var client = _httpClientFactory.CreateClient();
      using var response = await client.GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
      response.EnsureSuccessStatusCode();

      using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
      {
        using var fileStream = _fileSystem.OpenFileStream(installerPath, FileMode.Create, FileAccess.Write);
        await contentStream.CopyToAsync(fileStream, cancellationToken);
      }

      // Validate hash
      _logger.LogInformation("Validating installer hash");
      using (var fileStream = _fileSystem.OpenFileStream(installerPath, FileMode.Open, FileAccess.Read))
      {
        var hashSHA256 = await SHA256.HashDataAsync(fileStream, cancellationToken);
        var computedHash = Convert.ToHexString(hashSHA256);
        if (!computedHash.Equals(metadata.InstallerSha256, StringComparison.OrdinalIgnoreCase))
        {
          _logger.LogError("Installer hash mismatch");
          _fileSystem.DeleteFile(installerPath);
          return null;
        }
      }

      // Make executable on Unix
      if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
      {
        var process = Process.Start(new ProcessStartInfo
        {
          FileName = "chmod",
          Arguments = $"+x \"{installerPath}\"",
          UseShellExecute = false,
          CreateNoWindow = true
        });
        process?.WaitForExit();
      }

      return installerPath;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error downloading installer");
      return null;
    }
  }

  private async Task<BundleMetadataDto?> FetchBundleMetadata(
    Uri serverUri,
    RuntimeId runtime,
    CancellationToken cancellationToken)
  {
    try
    {
      var client = _httpClientFactory.CreateClient();
      client.BaseAddress = serverUri;

      using var response = await client.GetAsync(
        $"/api/agent-update/get-bundle-metadata/{runtime}",
        cancellationToken);

      if (!response.IsSuccessStatusCode)
      {
        _logger.LogWarning("Failed to fetch bundle metadata: {StatusCode}", response.StatusCode);
        return null;
      }

      var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);

      var metadata = JsonSerializer.Deserialize<BundleMetadataDto>(jsonContent, _jsonOptions);
      return metadata;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error fetching bundle metadata");
      return null;
    }
  }

  private async Task<List<(string UserName, string Uid)>> GetLoggedInUsers(CancellationToken cancellationToken)
  {
    var result = await _processManager.GetProcessOutput("who", "-u", timeoutMs: 10_000);
    if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.Value))
    {
      return [];
    }

    var users = new List<(string UserName, string Uid)>();

    foreach (var line in result.Value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      if (parts.Length == 0)
      {
        continue;
      }

      var userName = parts[0];
      var uidResult = await _processManager.GetProcessOutput("id", $"-u {QuoteArgument(userName)}", timeoutMs: 5_000);
      if (!uidResult.IsSuccess)
      {
        continue;
      }

      var uid = uidResult.Value.Trim();
      if (!int.TryParse(uid, out var numericUid) || numericUid < 500)
      {
        continue;
      }

      users.Add((userName, uid));
    }

    return [.. users.Distinct()];
  }

  private async Task<IProcess?> LaunchInstaller(string installerPath, string installCommand, CancellationToken cancellationToken)
  {
    switch (_systemEnvironment.Platform)
    {
      case SystemPlatform.Windows:
        return _processManager.Start(installerPath, installCommand);

      case SystemPlatform.Linux:
        await _processManager
          .Start("sudo", $"chmod +x {installerPath}")
          .WaitForExitAsync(cancellationToken);

        // Use systemd-run to launch installer in a separate scope to prevent
        // it from being killed when the agent service stops
        var systemdRunCommand = $"--scope {installerPath} {installCommand}";
        return _processManager.Start(
          "sudo",
          $"systemd-run {systemdRunCommand}",
          useShellExec: false);

      case SystemPlatform.MacOs:
        await _processManager
          .Start("sudo", $"chmod +x {installerPath}")
          .WaitForExitAsync(cancellationToken);

        return _processManager.Start(
          "/bin/zsh",
          $"-c \"{installerPath} {installCommand} &\"",
          useShellExec: true);

      default:
        throw new PlatformNotSupportedException();
    }
  }

  private async Task TryRunCommand(string fileName, string arguments, CancellationToken cancellationToken)
  {
    try
    {
      await _processManager.StartAndWaitForExit(fileName, arguments, useShellExec: false, cancellationToken);
    }
    catch (Exception ex)
    {
      _logger.LogInformation(ex, "Legacy desktop cleanup command failed: {FileName} {Arguments}", fileName, arguments);
    }
  }
}
