using ControlR.Libraries.Shared.Constants;
using ControlR.Libraries.NativeInterop.Unix;

namespace ControlR.DesktopClient.Mac;

public static class PathConstants
{
  public static string GetLogsPath(string? instanceId)
  {
    var isRoot = Libc.Geteuid() == 0;
    var rootDir = isRoot
       ? "/var/log/controlr"
       : $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/.controlr";

    rootDir = AppendInstanceId(rootDir, instanceId);
    var logsDir = isRoot ? rootDir : Path.Combine(rootDir, "logs");
    return Path.Combine(logsDir, "ControlR.DesktopClient", "LogFile.log");
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

}