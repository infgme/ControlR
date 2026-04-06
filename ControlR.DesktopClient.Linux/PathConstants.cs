using ControlR.Libraries.Shared.Constants;
using ControlR.Libraries.NativeInterop.Unix;

namespace ControlR.DesktopClient.Linux;

public static class PathConstants
{
  public static string GetLogsPath(string? instanceId)
  {
    var isRoot = Libc.Geteuid() == 0;
    var rootDir = isRoot
       ? "/var/log/controlr"
       : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".controlr");

    rootDir = AppendInstanceId(rootDir, instanceId);
    var logsDir = isRoot ? rootDir : Path.Combine(rootDir, "logs");
    return Path.Combine(logsDir, "ControlR.DesktopClient", "LogFile.log");
  }
  public static string GetWaylandRemoteDesktopRestoreTokenPath(string? instanceId)
  {
    var dir = GetSettingsDirectory(instanceId);
    return Path.Combine(dir, "wayland-remotedesktop-restore-token");
  }


  private static string AppendInstanceId(string rootDir, string? instanceId)
  {
    return Path.Combine(rootDir, GetEffectiveInstanceId(instanceId));
  }

  private static string GetEffectiveInstanceId(string? instanceId)
  {
    return string.IsNullOrWhiteSpace(instanceId)
      ? AppConstants.DefaultInstallDirectoryName
      : instanceId;
  }

  private static string GetSettingsDirectory(string? instanceId)
  {
    var rootDir = Libc.Geteuid() == 0
      ? "/etc/controlr"
      : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".controlr");

    return AppendInstanceId(rootDir, instanceId);
  }
}