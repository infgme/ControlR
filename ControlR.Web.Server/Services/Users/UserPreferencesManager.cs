using System.Collections.Frozen;
using ControlR.Web.Server.Primitives;
using ControlR.Web.Server.Services.Settings;

namespace ControlR.Web.Server.Services.Users;

public interface IUserPreferencesManager
{
  Task<HttpResult<UserPreferenceResponseDto>> SetPreference(
    Guid userId,
    UserPreferenceRequestDto preference,
    CancellationToken cancellationToken = default);
}

public class UserPreferencesManager(
  AppDb appDb,
  IEnumerable<IUserPreferenceValueHandler> handlers) : IUserPreferencesManager
{
  private readonly AppDb _appDb = appDb;
  private readonly FrozenDictionary<string, IUserPreferenceValueHandler> _handlers = handlers.ToHandlerDictionary();

  public async Task<HttpResult<UserPreferenceResponseDto>> SetPreference(
    Guid userId,
    UserPreferenceRequestDto preference,
    CancellationToken cancellationToken = default)
  {
    var user = await _appDb.Users
      .Include(x => x.UserPreferences)
      .FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);

    if (user is null)
    {
      return HttpResult.Fail<UserPreferenceResponseDto>(HttpResultErrorCode.NotFound, "User not found.");
    }

    var normalizationResult = NormalizePreferenceValue(preference);
    if (!normalizationResult.IsSuccess)
    {
      return normalizationResult.ToHttpResult(new UserPreferenceResponseDto(null, preference.Name, null));
    }

    user.UserPreferences ??= [];
    var normalizedValue = normalizationResult.Value ?? string.Empty;
    var existingPreference = user.UserPreferences.FirstOrDefault(x => x.Name == preference.Name);
    if (existingPreference is not null)
    {
      existingPreference.Value = normalizedValue;
      await _appDb.SaveChangesAsync(cancellationToken);
      return HttpResult.Ok(existingPreference.ToDto());
    }

    var entity = new UserPreference
    {
      Name = preference.Name,
      UserId = userId,
      Value = normalizedValue
    };

    user.UserPreferences.Add(entity);
    await _appDb.SaveChangesAsync(cancellationToken);
    return HttpResult.Ok(entity.ToDto());
  }

  private HttpResult<string?> NormalizePreferenceValue(UserPreferenceRequestDto preference)
  {
    if (_handlers.TryGetValue(preference.Name, out var handler))
    {
      return handler.ValidateAndNormalize(preference.Value);
    }

    return HttpResult.Ok<string?>(preference.Value.Trim());
  }
}