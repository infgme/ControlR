namespace ControlR.Agent.Shared.Interfaces;

public interface IDeviceInfoProvider
{
  Task<DeviceUpdateRequestDto> GetDeviceInfo();
}
