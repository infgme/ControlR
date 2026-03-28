using System.Security.Cryptography;
using ControlR.Libraries.Shared.Services.FileSystem;
using ControlR.Libraries.Shared.Services.Http;
using Microsoft.Extensions.Logging;

namespace ControlR.Agent.Installer.Services;

internal interface IBundleDownloader
{
  Task DownloadBundle(
    string bundleDownloadPath,
    string expectedSha256,
    string destinationPath,
    CancellationToken cancellationToken = default);
}

/// <summary>
/// Handles downloading and validating bundle ZIPs from the server.
/// </summary>
internal class BundleDownloader(
  IDownloadsApi downloadsApi,
  IFileSystem fileSystem,
  ILogger<BundleDownloader> logger)
  : IBundleDownloader
{
  private readonly IDownloadsApi _downloadsApi = downloadsApi;
  private readonly IFileSystem _fileSystem = fileSystem;
  private readonly ILogger<BundleDownloader> _logger = logger;

  /// <summary>
  /// Downloads a bundle ZIP file for the current runtime and validates its SHA-256 hash.
  /// </summary>
  public async Task DownloadBundle(
    string bundleDownloadPath,
    string expectedSha256,
    string destinationPath,
    CancellationToken cancellationToken = default)
  {
    var destinationDirectory = Path.GetDirectoryName(destinationPath)
      ?? throw new DirectoryNotFoundException("Unable to determine the bundle download directory.");

    _fileSystem.CreateDirectory(destinationDirectory);

    _logger.LogInformation("Downloading bundle from {Path}", bundleDownloadPath);

    var downloadResult = await _downloadsApi.DownloadFile(bundleDownloadPath, destinationPath, cancellationToken);
    if (!downloadResult.IsSuccess)
    {
      throw new InvalidOperationException(downloadResult.Reason);
    }

    var fileInfo = _fileSystem.GetFileInfo(destinationPath);
    _logger.LogInformation("Bundle size: {Size} bytes", fileInfo.Length);

    _logger.LogInformation("Validating bundle SHA-256...");
    await using var bundleStream = _fileSystem.OpenFileStream(destinationPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    var computedSha256 = await SHA256.HashDataAsync(bundleStream, cancellationToken);
    var computedHash = Convert.ToHexString(computedSha256);

    if (!computedHash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
    {
      throw new InvalidOperationException(
        $"Bundle hash mismatch. Expected: {expectedSha256}, Computed: {computedHash}");
    }

    _logger.LogInformation("Bundle hash validated successfully.");
  }
}
