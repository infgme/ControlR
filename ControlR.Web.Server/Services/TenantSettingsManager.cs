using System.Collections.Frozen;
using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Web.Server.Services.Settings;
using ControlR.Web.Server.Primitives;

namespace ControlR.Web.Server.Services;

public interface ITenantSettingsManager
{
  Task<HttpResult<TenantSettingResponseDto>> SetSetting(
    Guid tenantId,
    TenantSettingRequestDto setting,
    CancellationToken cancellationToken = default);
}

public class TenantSettingsManager(
  AppDb appDb,
  IEnumerable<ITenantSettingValueHandler> handlers) : ITenantSettingsManager
{
  private readonly AppDb _appDb = appDb;
  private readonly FrozenDictionary<string, ITenantSettingValueHandler> _handlers = handlers.ToHandlerDictionary();

  public async Task<HttpResult<TenantSettingResponseDto>> SetSetting(
    Guid tenantId,
    TenantSettingRequestDto setting,
    CancellationToken cancellationToken = default)
  {
    var tenant = await _appDb.Tenants
      .Include(x => x.TenantSettings)
      .FirstOrDefaultAsync(x => x.Id == tenantId, cancellationToken);

    if (tenant is null)
    {
      return HttpResult.Fail<TenantSettingResponseDto>(HttpResultErrorCode.NotFound, "Tenant not found.");
    }

    var normalizationResult = NormalizeSettingValue(setting);
    if (!normalizationResult.IsSuccess)
    {
      return normalizationResult.ToHttpResult(new TenantSettingResponseDto(null, setting.Name, null));
    }

    tenant.TenantSettings ??= [];
    var handler = _handlers.GetValueOrDefault(setting.Name);
    if (handler?.DeleteWhenValueIsNull == true && normalizationResult.Value is null)
    {
      var existingInstanceIdSetting = tenant.TenantSettings.FirstOrDefault(x => x.Name == setting.Name);
      if (existingInstanceIdSetting is not null)
      {
        tenant.TenantSettings.Remove(existingInstanceIdSetting);
        await _appDb.SaveChangesAsync(cancellationToken);
      }

      return HttpResult.Ok(new TenantSettingResponseDto(null, setting.Name, null));
    }

    var normalizedValue = normalizationResult.Value ?? string.Empty;
    var existingSetting = tenant.TenantSettings.FirstOrDefault(x => x.Name == setting.Name);
    if (existingSetting is not null)
    {
      existingSetting.Value = normalizedValue;
      await _appDb.SaveChangesAsync(cancellationToken);
      return HttpResult.Ok(existingSetting.ToDto());
    }

    var entity = new TenantSetting
    {
      Name = setting.Name,
      Value = normalizedValue,
      TenantId = tenantId
    };

    tenant.TenantSettings.Add(entity);
    await _appDb.SaveChangesAsync(cancellationToken);
    return HttpResult.Ok(entity.ToDto());
  }

  private HttpResult<string?> NormalizeSettingValue(TenantSettingRequestDto setting)
  {
    if (_handlers.TryGetValue(setting.Name, out var handler))
    {
      return handler.ValidateAndNormalize(setting.Value);
    }

    return HttpResult.Ok<string?>(setting.Value.Trim());
  }
}