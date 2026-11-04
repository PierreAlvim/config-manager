namespace ConfigManager.Distribution.Api.Data;

/// <summary>A published snapshot as stored: version plus the raw JSON content.</summary>
public sealed record RawSnapshot(long Version, string Json);

public sealed record PairVersion(string Namespace, string Environment, long Version);

public sealed record AccessGrant(string ClientId, string Namespace, string Environment);

/// <summary>Read-only view over the tables owned by the Management API.</summary>
public interface ISnapshotSource
{
    Task<RawSnapshot?> GetLatestAsync(string ns, string env, CancellationToken ct);
    Task<IReadOnlyList<PairVersion>> GetLatestVersionsAsync(CancellationToken ct);
    Task<IReadOnlyList<AccessGrant>> GetGrantsAsync(CancellationToken ct);
    Task PingAsync(CancellationToken ct);
}
