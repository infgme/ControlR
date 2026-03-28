namespace ControlR.Agent.Shared.Constants;

public static class PathConstants
{
  public const string MacApplicationsDirectory = "/Applications";
  public const string MacBundleStateDirectory = "/Library/Application Support/ControlR";
  public static string MacDesktopExecutableRelativePath => "Contents/MacOS/ControlR.DesktopClient";

  public static string GetMacBundleStateDirectory(string? instanceId)
  {
    return string.IsNullOrWhiteSpace(instanceId)
      ? MacBundleStateDirectory
      : $"{MacBundleStateDirectory}/{instanceId}";
  }

  public static string GetMacInstalledAppPath(string? instanceId)
  {
    var appBundleName = string.IsNullOrWhiteSpace(instanceId)
      ? "ControlR.app"
      : $"ControlR.{instanceId}.app";

    return $"{MacApplicationsDirectory}/{appBundleName}";
  }
}