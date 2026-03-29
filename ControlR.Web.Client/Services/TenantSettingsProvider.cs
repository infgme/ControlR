using System.Collections.Concurrent;
using System.Net;
using ControlR.Libraries.Shared.DataValidation;

namespace ControlR.Web.Client.Services;

public interface ITenantSettingsProvider
{
  Task<bool> GetAppendInstanceId();
  Task<string?> GetInstanceId();
  Task<bool?> GetNotifyUserOnSessionStart();
  Task SetAppendInstanceId(bool value);
  Task<bool> SetInstanceId(string? value);
  Task SetNotifyUserOnSessionStart(bool? value);
}

internal class TenantSettingsProvider(
  IControlrApi controlrApi,
  ISnackbar snackbar,
  ILogger<TenantSettingsProvider> logger) : ITenantSettingsProvider
{
  private readonly IControlrApi _controlrApi = controlrApi;
  private readonly ILogger<TenantSettingsProvider> _logger = logger;

  private readonly ConcurrentDictionary<string, object?> _settings = new();
  private readonly ISnackbar _snackbar = snackbar;

  public async Task<bool> GetAppendInstanceId()
  {
    return await GetSetting(TenantSettingNames.AppendInstanceId, false);
  }

  public async Task<string?> GetInstanceId()
  {
    return await GetSetting<string?>(TenantSettingNames.InstanceId, null);
  }

  public async Task<bool?> GetNotifyUserOnSessionStart()
  {
    return await GetSetting(TenantSettingNames.NotifyUserOnSessionStart, (bool?)null);
  }

  public async Task SetAppendInstanceId(bool value)
  {
    await SetSetting(TenantSettingNames.AppendInstanceId, value);
  }

  public async Task<bool> SetInstanceId(string? value)
  {
    var normalizedValue = string.IsNullOrWhiteSpace(value)
      ? null
      : value.Trim();

    if (!string.IsNullOrWhiteSpace(normalizedValue) &&
        !Validators.ValidateInstanceId(normalizedValue, out var illegalCharacters))
    {
      var message = $"Instance ID contains one or more invalid characters: {string.Join(", ", illegalCharacters)}";
      _logger.LogWarning("Rejected invalid instance ID. Invalid characters: {IllegalCharacters}", string.Join(", ", illegalCharacters));
      _snackbar.Add(message, Severity.Error);
      return false;
    }

    await SetSetting(TenantSettingNames.InstanceId, normalizedValue);
    return true;
  }

  public async Task SetNotifyUserOnSessionStart(bool? value)
  {
    await SetSetting(TenantSettingNames.NotifyUserOnSessionStart, value);
  }

  private async Task<T> GetSetting<T>(string settingName, T defaultValue)
  {
    try
    {
      if (_settings.TryGetValue(settingName, out var value) &&
          value is T typedValue)
      {
        return typedValue;
      }

      var getResult = await _controlrApi.TenantSettings.GetTenantSetting(settingName);

      if (!getResult.IsSuccess)
      {
        if (getResult.StatusCode == HttpStatusCode.NotFound)
        {
          return defaultValue;
        }

        _snackbar.Add(getResult.Reason, Severity.Error);
        return defaultValue;
      }

      if (getResult.Value is null)
      {
        return defaultValue;
      }

      if (!getResult.Value.HasValueSet)
      {
        return defaultValue;
      }

      var targetType = typeof(T);

      if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
      {
        // Get the underlying type (e.g., bool from bool?)
        targetType = Nullable.GetUnderlyingType(targetType) ??
          throw new InvalidOperationException($"Failed to convert setting value to type {targetType.Name}.");
      }

      if (Convert.ChangeType(getResult.Value.Value, targetType) is not T typedResult)
      {
        return defaultValue;
      }

      _settings[settingName] = typedResult;
      return typedResult;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error while getting setting for {SettingName}.", settingName);
      _snackbar.Add("Error while getting setting", Severity.Error);
      return defaultValue;
    }
  }

  private async Task SetSetting<T>(string settingName, T newValue)
  {
    try
    {
      _settings[settingName] = newValue;
      
      if (newValue is null)
      {
        var deleteResult = await _controlrApi.TenantSettings.DeleteTenantSetting(settingName);
        if (!deleteResult.IsSuccess)
        {
          _logger.LogError("Failed to delete setting.  Reason: {Reason}, StatusCode: {StatusCode}",
            deleteResult.Reason,
            deleteResult.StatusCode);

          _snackbar.Add(deleteResult.Reason, Severity.Error);
        }
        return;
      }
      
      var stringValue = Convert.ToString(newValue);
      Guard.IsNotNull(stringValue);
      var request = new TenantSettingRequestDto(settingName, stringValue);
      var setResult = await _controlrApi.TenantSettings.SetTenantSetting(request);

      if (!setResult.IsSuccess)
      {
        _logger.LogError("Failed to set setting.  Reason: {Reason}, StatusCode: {StatusCode}",
          setResult.Reason,
          setResult.StatusCode);
          
        _snackbar.Add(setResult.Reason, Severity.Error);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error while setting setting for {SettingName}.", settingName);
      _snackbar.Add("Error while setting setting", Severity.Error);
    }
  }
}
