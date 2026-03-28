using ControlR.Libraries.Api.Contracts.Dtos.IpcDtos;
using Microsoft.Extensions.Hosting;

namespace ControlR.DesktopClient.Common.ServiceInterfaces;

public interface IRemoteControlHostBuilderFactory
{
  HostApplicationBuilder CreateHostBuilder(RemoteControlRequestIpcDto requestDto);
}
