namespace ControlR.DesktopClient.Services;

internal sealed class ShellNavigationItemProvider : INavigationItemProvider
{
  public IEnumerable<NavigationItemDescriptor> GetNavigationItems()
  {
    return
    [
      new(typeof(IConnectionsViewModel), "home_regular", Localization.Connections, 0),
      new(typeof(ISettingsViewModel), "settings_regular", Localization.Settings, 200),
      new(typeof(IAboutViewModel), "question_circle_regular", Localization.About, 300)
    ];
  }
}
