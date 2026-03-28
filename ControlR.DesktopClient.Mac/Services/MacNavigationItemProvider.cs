using ControlR.DesktopClient.Common;
using ControlR.DesktopClient.Common.ServiceInterfaces;
using ControlR.DesktopClient.ViewModels.Mac;

namespace ControlR.DesktopClient.Mac.Services;

public class MacNavigationItemProvider : INavigationItemProvider
{
  public IEnumerable<NavigationItemDescriptor> GetNavigationItems()
  {
    return
    [
      new(typeof(IPermissionsViewModelMac), "shield_keyhole_regular", Localization.Permissions, 100)
    ];
  }
}
