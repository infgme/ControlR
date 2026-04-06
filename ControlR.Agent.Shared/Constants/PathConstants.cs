namespace ControlR.Agent.Shared.Constants;

public static class PathConstants
{
  public const string MacApplicationsDirectory = "/Applications";
  public static string MacDesktopExecutableRelativePath => "Contents/MacOS/ControlR.DesktopClient";

  public static string GetMacInstalledAppPath(string? instanceId)
  {
    var appBundleName = string.IsNullOrWhiteSpace(instanceId)
      ? "ControlR.app"
      : $"ControlR.{instanceId}.app";

    return $"{MacApplicationsDirectory}/{appBundleName}";
  }
}