using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControlR.DesktopClient.Linux.Services;
using ControlR.DesktopClient.ViewModels;
using ControlR.DesktopClient.Views.Linux;
using Microsoft.Extensions.DependencyInjection;

namespace ControlR.DesktopClient.ViewModels.Linux;

public interface IPermissionsViewModelWayland : IPermissionsViewModel
{
  IAsyncRelayCommand GrantRemoteControlPermissionCommand { get; }
  bool IsRemoteControlPermissionGranted { get; }
  Task SetPermissionValues();
}

public partial class PermissionsViewModelWayland(IServiceProvider serviceProvider) : ViewModelBase<PermissionsViewWayland>, IPermissionsViewModelWayland
{
  private readonly IServiceProvider _serviceProvider = serviceProvider;

  [ObservableProperty]
  private bool _isRemoteControlPermissionGranted;

  public async Task SetPermissionValues()
  {
    var waylandPermissions = _serviceProvider.GetRequiredService<IWaylandPermissionProvider>();
    var isGranted = await waylandPermissions.IsRemoteControlPermissionGranted().ConfigureAwait(false);
    IsRemoteControlPermissionGranted = isGranted;
  }

  protected override async Task OnInitializeAsync()
  {
    await base.OnInitializeAsync();
    await SetPermissionValues();
  }

  [RelayCommand]
  private async Task GrantRemoteControlPermission()
  {
    var accessor = _serviceProvider.GetRequiredService<IWaylandPermissionProvider>();
    await accessor.RequestRemoteControlPermission(bypassRestoreToken: true);
    await SetPermissionValues();
  }
}
