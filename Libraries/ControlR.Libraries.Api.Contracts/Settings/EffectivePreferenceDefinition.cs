using ControlR.Libraries.Api.Contracts.Constants;

namespace ControlR.Libraries.Api.Contracts.Settings;

public sealed record EffectivePreferenceDefinition<T>(
  string UserPreferenceName,
  string? TenantSettingName,
  T DefaultValue);

public static class EffectivePreferenceDefinitions
{
  public static EffectivePreferenceDefinition<bool> NotifyUserOnSessionStart { get; } =
    new(
      UserPreferenceNames.NotifyUserOnSessionStart,
      TenantSettingNames.NotifyUserOnSessionStart,
      true);
}