namespace ControlR.Web.Server.Services;

public interface IAgentVersionProvider
{
  Task<Result<Version>> TryGetAgentVersion(CancellationToken cancellationToken = default);
}

public class AgentVersionProvider(
  IWebHostEnvironment webHostEnvironment,
  ILogger<AgentVersionProvider> logger) : IAgentVersionProvider
{
  private static readonly SemaphoreSlim _versionLock = new(1, 1);
  private static volatile Version? _cachedVersion;

  public async Task<Result<Version>> TryGetAgentVersion(CancellationToken cancellationToken = default)
  {
    try
    {
      if (webHostEnvironment.IsDevelopment())
      {
        _cachedVersion = typeof(AgentVersionProvider).Assembly.GetName()?.Version;
      }
      
      if (_cachedVersion is not null)
      {
        return Result.Ok(_cachedVersion);
      }

      using var heldLock = await _versionLock.AcquireLockAsync(cancellationToken);

      // Double-check after acquiring lock to avoid redundant file access if another thread already populated the cache.
      if (_cachedVersion is not null)
      {
        return Result.Ok(_cachedVersion);
      }

      var fileInfo = webHostEnvironment.WebRootFileProvider.GetFileInfo("/downloads/Version.txt");

      if (!fileInfo.Exists || string.IsNullOrWhiteSpace(fileInfo.PhysicalPath))
      {
        logger.LogError("Agent version file not found at path: {Path}", fileInfo.PhysicalPath);
        return Result.Fail<Version>("Version file not found.");
      }

      await using var fs = new FileStream(fileInfo.PhysicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
      using var sr = new StreamReader(fs);
      var versionString = await sr.ReadToEndAsync(cancellationToken);

      if (!Version.TryParse(versionString?.Trim(), out var version))
      {
        logger.LogError("Invalid version format in file: {VersionString}", versionString);
        return Result.Fail<Version>("Invalid version format.");
      }
      _cachedVersion = version;
      return Result.Ok(version);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Error retrieving agent version.");
      return Result.Fail<Version>("Error retrieving agent version.");
    }
  }
}