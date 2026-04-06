namespace ControlR.DesktopClient.Common.Startup;

public static class ArgsParser
{
  private static readonly Dictionary<string, string> _args = new(StringComparer.OrdinalIgnoreCase);

  public static TValue GetArgValue<TValue>(string argName, TValue? defaultValue = default)
  {
    BuildArgs();

    return GetArgValue(_args, argName, defaultValue);
  }

  internal static TValue GetArgValue<TValue>(IReadOnlyDictionary<string, string> args, string argName, TValue? defaultValue = default)
  {
    var sanitizedName = argName.TrimStart('-', '-');

    if (!args.TryGetValue(sanitizedName, out var value))
    {
      if (defaultValue is not null)
      {
        return defaultValue;
      }
      throw new ArgumentException($"Argument '{sanitizedName}' not found.");
    }

    var targetType = typeof(TValue);
    if (targetType.IsEnum)
    {
      return (TValue)Enum.Parse(targetType, value, ignoreCase: true);
    }

    if (targetType == typeof(bool) && string.IsNullOrEmpty(value))
    {
      return (TValue)(object)true;
    }

    return (TValue)Convert.ChangeType(value, targetType);
  }

  internal static Dictionary<string, string> ParseArgs(string[] args)
  {
    var parsedArgs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i < args.Length; i++)
    {
      if (!args[i].StartsWith("--", StringComparison.Ordinal))
      {
        continue;
      }

      var argName = args[i][2..];
      if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
      {
        parsedArgs[argName] = args[i + 1];
      }
      else
      {
        parsedArgs[argName] = string.Empty;
      }
    }

    return parsedArgs;
  }

  private static void BuildArgs()
  {
    lock (_args)
    {
      if (_args.Count > 0)
      {
        return;
      }

      foreach (var (key, value) in ParseArgs(Environment.GetCommandLineArgs()))
      {
        _args[key] = value;
      }
    }
  }
}
