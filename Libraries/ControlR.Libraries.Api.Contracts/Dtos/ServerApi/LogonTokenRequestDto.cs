namespace ControlR.Libraries.Api.Contracts.Dtos.ServerApi;

public record LogonTokenRequestDto(
  Guid DeviceId,
  int ExpirationMinutes = 15);