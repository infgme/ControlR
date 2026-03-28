using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using ControlR.Agent.Common.Models;
using ControlR.Agent.Common.Services;
using ControlR.Agent.Common.Startup;
using ControlR.Agent.Shared.Interfaces;
using ControlR.Agent.Shared.Services;
using ControlR.Libraries.Shared.DataValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ControlR.Agent.Startup;

internal static class CommandProvider
{
  internal static Command GetInstallCommand(string[] args)
  {
    var serverUriOption = CreateServerUriOption(required: false);
    var instanceIdOption = CreateInstanceIdOption();
    var deviceTagsOption = new Option<string?>("-g", "--device-tags")
    {
      Description = "An optional, comma-separated list of tags to which the agent will be assigned."
    };
    var tenantIdOption = new Option<Guid?>("-t", "--tenant-id")
    {
      Description = "The tenant ID to which the agent will be assigned."
    };
    var installerKeySecretOption = new Option<string?>("-ks", "--installer-key-secret")
    {
      Description = "An access key that will allow the device to be created on the server."
    };
    var installerKeyIdOption = new Option<Guid?>("-ki", "--installer-key-id")
    {
      Description = "The ID of the installer key to use for installation."
    };
    var deviceIdOption = new Option<Guid?>("-d", "--device-id")
    {
      Description = "An optional device ID to which the agent will be assigned."
    };

    var installCommand = new Command("install", "Download and launch the ControlR bootstrap installer.")
    {
      serverUriOption,
      instanceIdOption,
      deviceTagsOption,
      tenantIdOption,
      installerKeySecretOption,
      installerKeyIdOption,
      deviceIdOption,
    };

    installCommand.SetAction(async parseResult =>
    {
      var instanceId = parseResult.GetValue(instanceIdOption);
      var requestedServerUri = parseResult.GetValue(serverUriOption);

      using var host = CreateHost(StartupMode.Install, args, instanceId, requestedServerUri);
      var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ControlR.Agent");

      try
      {
        var bridge = host.Services.GetRequiredService<ILegacyInstallerBridge>();
        var settingsProvider = host.Services.GetRequiredService<ISettingsProvider>();
        var serverUri = requestedServerUri ?? settingsProvider.ServerUri;
        var tenantId = parseResult.GetValue(tenantIdOption) ?? settingsProvider.GetRequiredTenantId();
        var deviceId = parseResult.GetValue(deviceIdOption);
        if (deviceId is null && settingsProvider.DeviceId != Guid.Empty)
        {
          deviceId = settingsProvider.DeviceId;
        }

        var tagIds = ParseTagIds(parseResult.GetValue(deviceTagsOption));

        var forwarded = await bridge.TryForwardToNewInstaller(
          serverUri,
          tenantId,
          parseResult.GetValue(installerKeySecretOption),
          parseResult.GetValue(installerKeyIdOption),
          deviceId,
          tagIds,
          instanceId);

        return forwarded ? 0 : 1;
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Failed to forward install command to the ControlR bootstrap installer.");
        return 1;
      }
    });

    return installCommand;
  }

  internal static Command GetRunCommand(string[] args)
  {
    var instanceIdOption = CreateInstanceIdOption();

    var runCommand = new Command("run", "Run the ControlR service.")
    {
      instanceIdOption
    };

    runCommand.SetAction(async parseResult =>
    {
      var instanceId = parseResult.GetValue(instanceIdOption);
      using var host = CreateHost(StartupMode.Run, args, instanceId);
      await host.RunAsync();
    });

    return runCommand;
  }

  internal static Command GetUninstallCommand(string[] args)
  {
    var instanceIdOption = CreateInstanceIdOption();

    var unInstallCommand = new Command("uninstall", "Uninstall the ControlR service.")
    {
      instanceIdOption
    };

    unInstallCommand.SetAction(async parseResult =>
    {
      var instanceId = parseResult.GetValue(instanceIdOption);
      using var host = CreateHost(StartupMode.Uninstall, args, instanceId);
      var installer = host.Services.GetRequiredService<IAgentInstaller>();

      await installer.Uninstall();

      await WaitForShutdown();
    });

    return unInstallCommand;
  }

  private static IHost CreateHost(
    StartupMode startupMode,
    string[] args,
    string? instanceId = null,
    Uri? serverUri = null)
  {
    var host = Host.CreateApplicationBuilder(args);

    host.AddControlRAgent(startupMode, instanceId, serverUri);
    return host.Build();
  }

  private static Option<string?> CreateInstanceIdOption()
  {
    var instanceIdOption = new Option<string?>("-i", "--instance-id")
    {
      Description = "The instance ID of the agent, which can be used for multiple agent installations."
    };

    instanceIdOption.Validators.Add(ValidateInstanceId);
    return instanceIdOption;
  }

  private static Option<Uri?> CreateServerUriOption(bool required)
  {
    return new Option<Uri?>("-s", "--server-uri")
    {
      Required = required,
      Description = "The fully-qualified server URI to which the agent will connect.",
      CustomParser = result =>
      {
        if (result.Tokens.Count == 0)
        {
          return null;
        }

        var uriArg = result.Tokens[0].Value;
        if (Uri.TryCreate(uriArg, UriKind.Absolute, out var uri))
        {
          return uri;
        }

        result.AddError(
          $"The server URI '{uriArg}' is not a valid absolute URI. Please provide a valid URI including the scheme (e.g. 'https://').");

        return null;
      }
    };
  }

  private static Guid[]? ParseTagIds(string? deviceTags)
  {
    return deviceTags is null
      ? null
      : [.. deviceTags
        .Split(',')
        .Select(x => Guid.TryParse(x, out var tagId)
          ? tagId
          : Guid.Empty)
        .Where(x => x != Guid.Empty)];
  }

  private static void ValidateInstanceId(OptionResult optionResult)
  {
    var id = optionResult.GetValueOrDefault<string?>();
    if (id is not null && !Validators.ValidateInstanceId(id, out var matchedIllegalChars))
    {
      optionResult.AddError(
        $"The instance ID contains one or more invalid characters: {string.Join(", ", matchedIllegalChars!)}");
    }
  }

  private static async Task<bool> WaitForKeyPress(TimeSpan timeout)
  {
    var sw = Stopwatch.StartNew();
    while (sw.Elapsed < timeout)
    {
      if (Console.KeyAvailable)
      {
        _ = Console.ReadKey(intercept: true);
        return true;
      }
      await Task.Delay(100);
    }
    return false;
  }

  private static async Task WaitForShutdown()
  {
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (sender, eventArgs) =>
    {
      cts.Cancel();
    };

    if (!Environment.UserInteractive)
    {
      Console.WriteLine("Installation completed.  Shutting down.");
      return;
    }

    var timeout = TimeSpan.FromSeconds(5);
    Console.WriteLine($"Application will exit in {timeout.TotalSeconds} seconds. Press any key to interrupt.");

    var keyPressed = await WaitForKeyPress(timeout);
    if (keyPressed)
    {
      Console.WriteLine("Shutdown cancelled by user. Application will continue running.  Press Ctrl+C to exit.");
      await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
    }
    else
    {
      Console.WriteLine("No key pressed; shutting down.");
      cts.Cancel();
    }
  }
}
