using System.Runtime.Versioning;
using ControlR.Libraries.TestingUtilities;

namespace ControlR.DesktopClient.Windows.Tests;

[SupportedOSPlatform("windows8.0")]
public class PathConstantsTests
{
  [WindowsOnlyFact]
  public void GetLogsPath_WhenInstanceIdMissing_UsesDefaultDirectory()
  {
    var result = PathConstants.GetLogsPath(instanceId: null);

    Assert.Contains(@"\ControlR\", result, StringComparison.OrdinalIgnoreCase);
    Assert.EndsWith(@"default\Logs\ControlR.DesktopClient\LogFile.log", result, StringComparison.OrdinalIgnoreCase);
  }
}