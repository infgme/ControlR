using System.Diagnostics.CodeAnalysis;

namespace ControlR.Libraries.Shared.DataValidation;

public static class Validators
{
  public static bool ValidateInstanceId(string instanceId, [NotNullWhen(false)] out char[]? illegalCharacters)
  {
    char[] allIllegalChars = [.. Path.GetInvalidPathChars(), ' '];
    illegalCharacters = [.. allIllegalChars.Where(c => instanceId.Contains(c))];
    return illegalCharacters.Length == 0;
  }
}