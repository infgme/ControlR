namespace ControlR.DesktopClient.Common.ServiceInterfaces;

public interface INavigationItemProvider
{
  IEnumerable<NavigationItemDescriptor> GetNavigationItems();
}
