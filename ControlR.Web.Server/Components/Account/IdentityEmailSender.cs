using Microsoft.AspNetCore.Identity.UI.Services;

namespace ControlR.Web.Server.Components.Account;

internal sealed class IdentityEmailSender(IEmailSender emailSender) : IEmailSender<AppUser>
{
  private readonly IEmailSender _emailSender = emailSender;

  public Task SendConfirmationLinkAsync(AppUser user, string email, string confirmationLink)
  {
    return _emailSender.SendEmailAsync(
      email,
      "ControlR Account Confirmation",
      $"Please confirm your ControlR account by following this link: <a href='{confirmationLink}'>{confirmationLink}</a>.");
  }

  public Task SendPasswordResetCodeAsync(AppUser user, string email, string resetCode)
  {
    return _emailSender.SendEmailAsync(
      email,
      "ControlR Password Reset",
      $"Please reset your ControlR password using the following code: {resetCode}");
  }

  public Task SendPasswordResetLinkAsync(AppUser user, string email, string resetLink)
  {
    return _emailSender.SendEmailAsync(
      email,
      "ControlR Password Reset",
      $"Please reset your ControlR password by following this link: <a href='{resetLink}'>{resetLink}</a>.");
  }
}