namespace ControlR.Agent.Shared.Models;

public sealed record AgentInstallRequest
{
  public string? BundleSha256 { get; init; }

  public required string BundleZipPath { get; init; }

  public required Uri ServerUri { get; init; }

  public required Guid TenantId { get; init; }

  public string? InstallerKeySecret { get; init; }

  public Guid? InstallerKeyId { get; init; }

  public Guid? DeviceId { get; init; }

  public Guid[] TagIds { get; init; } = [];
}