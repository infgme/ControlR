namespace ControlR.Libraries.Api.Contracts.Dtos.ServerApi;

public record LogonTokenResponseDto(
  Uri DeviceAccessUrl,
  DateTimeOffset ExpiresAt,
  string Token);