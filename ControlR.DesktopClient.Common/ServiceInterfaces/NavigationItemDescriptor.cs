using ControlR.DesktopClient.Common.ViewModelInterfaces;

namespace ControlR.DesktopClient.Common.ServiceInterfaces;

public sealed record NavigationItemDescriptor(
  Type ViewModelType,
  string IconKey,
  string Label,
  int Order)
{
  public void ThrowIfInvalid()
  {
    if (!typeof(IViewModelBase).IsAssignableFrom(ViewModelType))
    {
      throw new InvalidOperationException($"{ViewModelType.FullName} does not implement {nameof(IViewModelBase)}.");
    }
  }
}
