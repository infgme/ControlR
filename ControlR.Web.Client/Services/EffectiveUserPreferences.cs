using ControlR.Libraries.Api.Contracts.Settings;
using ControlR.Web.Client.Models;

namespace ControlR.Web.Client.Services;

public interface IEffectiveUserPreferences
{
  Task<EffectivePreference<bool>> GetNotifyUserOnSessionStart();
}

internal sealed class EffectiveUserPreferences(
  ITenantSettingsProvider tenantSettingsProvider,
  IUserPreferencesProvider userPreferencesProvider) : IEffectiveUserPreferences
{
  private readonly ITenantSettingsProvider _tenantSettingsProvider = tenantSettingsProvider;
  private readonly IUserPreferencesProvider _userPreferencesProvider = userPreferencesProvider;

  public async Task<EffectivePreference<bool>> GetNotifyUserOnSessionStart()
  {
    return await ResolveBoolean(
      EffectivePreferenceDefinitions.NotifyUserOnSessionStart,
      _tenantSettingsProvider.GetNotifyUserOnSessionStart,
      _userPreferencesProvider.GetNotifyUserOnSessionStart);
  }

  private static async Task<EffectivePreference<bool>> ResolveBoolean(
    EffectivePreferenceDefinition<bool> definition,
    Func<Task<bool?>> getTenantSetting,
    Func<Task<bool>> getUserPreference)
  {
    var tenantValue = definition.TenantSettingName is null
      ? null
      : await getTenantSetting();

    if (tenantValue.HasValue)
    {
      return new EffectivePreference<bool>(tenantValue.Value, true);
    }

    var userValue = await getUserPreference();
    return new EffectivePreference<bool>(userValue, false);
  }
}