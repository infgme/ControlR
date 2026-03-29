using System.Collections.Frozen;
using ControlR.Web.Server.Primitives;

namespace ControlR.Web.Server.Services.Settings;

public interface INamedStringValueHandler
{
  bool DeleteWhenValueIsNull => false;

  string Name { get; }

  HttpResult<string?> ValidateAndNormalize(string value);
}

public interface ITenantSettingValueHandler : INamedStringValueHandler;

public interface IUserPreferenceValueHandler : INamedStringValueHandler;

internal static class NamedStringValueHandlerExtensions
{
  public static FrozenDictionary<string, THandler> ToHandlerDictionary<THandler>(this IEnumerable<THandler> handlers)
    where THandler : INamedStringValueHandler
  {
    return handlers.ToFrozenDictionary(x => x.Name, StringComparer.Ordinal);
  }
}

internal static class NamedStringValueHandlerResults
{
  public static HttpResult<string?> NormalizeBoolean(string value, string settingName)
  {
    if (!bool.TryParse(value, out var parsedValue))
    {
      return HttpResult.Fail<string?>(
        HttpResultErrorCode.ValidationFailed,
        $"{settingName} must be a valid boolean value.");
    }

    return HttpResult.Ok<string?>(parsedValue.ToString());
  }

  public static HttpResult<string?> NormalizeEnum<TEnum>(string value, string settingName)
    where TEnum : struct, Enum
  {
    if (!Enum.TryParse<TEnum>(value, true, out var parsedValue) || !Enum.IsDefined(parsedValue))
    {
      return HttpResult.Fail<string?>(
        HttpResultErrorCode.ValidationFailed,
        $"{settingName} must be a valid {typeof(TEnum).Name} value.");
    }

    return HttpResult.Ok<string?>(parsedValue.ToString());
  }
}