using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using ControlR.DesktopClient.ViewModels.Mac;
using ControlR.Libraries.Shared.Extensions;

namespace ControlR.DesktopClient.Views.Mac;

public partial class PermissionsViewMac : UserControl
{
  private Window? _mainWindow;

  public PermissionsViewMac()
  {
    InitializeComponent();
  }

  public PermissionsViewMac(IPermissionsViewModelMac viewModel)
  {
    DataContext = viewModel;
    InitializeComponent();
    _mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    if (_mainWindow is null)
    {
      return;
    }

    _mainWindow.Activated += MainWindowActivated;
    Unloaded += ViewUnloaded;
  }

  private void MainWindowActivated(object? sender, EventArgs e)
  {
    if (DataContext is not IPermissionsViewModelMac viewModel)
    {
      return;
    }

    viewModel.SetPermissionValues().Forget();
  }

  private void ViewUnloaded(object? sender, RoutedEventArgs e)
  {
    if (_mainWindow is not null)
    {
      _mainWindow.Activated -= MainWindowActivated;
    }
  }
}
