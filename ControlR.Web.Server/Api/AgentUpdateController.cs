using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using ControlR.Libraries.Api.Contracts.Constants;
using Microsoft.AspNetCore.OutputCaching;

namespace ControlR.Web.Server.Api;

[ApiController]
[Route(HttpConstants.AgentUpdateEndpoint)]
public class AgentUpdateController(
  ILogger<AgentUpdateController> logger,
  IWebHostEnvironment webHostEnvironment) : ControllerBase
{
  private const int CacheDurationSeconds = 600;

  private readonly ILogger<AgentUpdateController> _logger = logger;
  private readonly IWebHostEnvironment _webHostEnvironment = webHostEnvironment;

  [OutputCache(Duration = CacheDurationSeconds)]
  [ResponseCache(Duration = CacheDurationSeconds, Location = ResponseCacheLocation.Any)]
  [Produces("application/json")]
  [HttpGet("get-bundle-metadata/{runtime}")]
  public async Task<ActionResult<BundleMetadataDto>> GetBundleMetadata(
    RuntimeId runtime, 
    [FromServices] IAgentVersionProvider agentVersionProvider,
    CancellationToken cancellationToken)
  {
    var bundlePath = GetBundleDownloadPath(runtime);
    var installerPath = GetInstallerDownloadPath(runtime);

    _logger.LogDebug("GetBundleMetadata request for runtime {Runtime}", runtime);

    var bundleFileInfo = _webHostEnvironment.WebRootFileProvider.GetFileInfo(bundlePath);
    var installerFileInfo = _webHostEnvironment.WebRootFileProvider.GetFileInfo(installerPath);

    var agentVersionResult = await agentVersionProvider.TryGetAgentVersion(cancellationToken);
    if (!agentVersionResult.IsSuccess)
    {
      _logger.LogWarning("Failed to get agent version: {Reason}", agentVersionResult.Reason);
      return StatusCode(StatusCodes.Status500InternalServerError, "Failed to retrieve agent version.");
    }

    if (!bundleFileInfo.Exists || bundleFileInfo.PhysicalPath is null)
    {
      _logger.LogWarning("Bundle file does not exist: {FilePath}", bundlePath);
      return NotFound("Bundle not found");
    }

    if (!installerFileInfo.Exists || installerFileInfo.PhysicalPath is null)
    {
      _logger.LogWarning("Installer file does not exist: {FilePath}", installerPath);
      return NotFound("Installer not found");
    }

    _logger.LogDebug("Computing bundle and installer hashes for {Runtime}", runtime);

    // Compute bundle hash
    await using var bundleStream = new FileStream(bundleFileInfo.PhysicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    var bundleHash = await SHA256.HashDataAsync(bundleStream, cancellationToken);
    var bundleSha256 = Convert.ToHexString(bundleHash);

    // Compute installer hash
    await using var installerStream = new FileStream(installerFileInfo.PhysicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    var installerHash = await SHA256.HashDataAsync(installerStream, cancellationToken);
    var installerSha256 = Convert.ToHexString(installerHash);

    var metadata = new BundleMetadataDto
    {
      Runtime = runtime,
      Version = agentVersionResult.Value,
      BundleDownloadUrl = bundlePath,
      BundleSha256 = bundleSha256,
      InstallerDownloadUrl = installerPath,
      InstallerSha256 = installerSha256,
    };

    return Ok(metadata);
  }

  [OutputCache(Duration = CacheDurationSeconds)]
  [ResponseCache(Duration = CacheDurationSeconds, Location = ResponseCacheLocation.Any)]
  [Produces("text/plain")]
  [HttpGet("get-hash-sha256/{runtime}")]
  public async Task<ActionResult<string>> GetHash(RuntimeId runtime, CancellationToken cancellationToken)
  {
    var filePath = AppConstants.GetAgentFileDownloadPath(runtime);
    _logger.LogDebug("GetHash request started for downloads file. Path: {FilePath}", filePath);

    var fileInfo = _webHostEnvironment.WebRootFileProvider.GetFileInfo(filePath);
    if (!fileInfo.Exists || fileInfo.PhysicalPath is null)
    {
      _logger.LogWarning("File does not exist: {FilePath}", filePath);
      return NotFound();
    }

    _logger.LogDebug("Calculating hash.");
    await using var fs = new FileStream(fileInfo.PhysicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    var sha256Hash = await SHA256.HashDataAsync(fs, cancellationToken);
    var hexHash = Convert.ToHexString(sha256Hash);

    return Ok(hexHash);
  }

  private static string GetBundleDownloadPath(RuntimeId runtime)
  {
    return AppConstants.GetBundleZipDownloadPath(runtime);
  }

  private static string GetInstallerDownloadPath(RuntimeId runtime)
  {
    return AppConstants.GetInstallerDownloadPath(runtime);
  }
}