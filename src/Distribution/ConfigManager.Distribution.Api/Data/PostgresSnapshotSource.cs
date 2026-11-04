using Npgsql;

namespace ConfigManager.Distribution.Api.Data;

public sealed class PostgresSnapshotSource(NpgsqlDataSource dataSource) : ISnapshotSource
{
    public async Task<RawSnapshot?> GetLatestAsync(string ns, string env, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand(
            """
            SELECT version, content::text FROM published_snapshots
            WHERE namespace_key = $1 AND environment_key = $2
            ORDER BY version DESC LIMIT 1
            """);
        cmd.Parameters.AddWithValue(ns);
        cmd.Parameters.AddWithValue(env);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new RawSnapshot(reader.GetInt64(0), reader.GetString(1)) : null;
    }

    public async Task<IReadOnlyList<PairVersion>> GetLatestVersionsAsync(CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand(
            """
            SELECT namespace_key, environment_key, max(version) FROM published_snapshots
            GROUP BY namespace_key, environment_key
            """);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<PairVersion>();
        while (await reader.ReadAsync(ct))
            list.Add(new PairVersion(reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
        return list;
    }

    public async Task<IReadOnlyList<AccessGrant>> GetGrantsAsync(CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand(
            "SELECT client_id, namespace_key, environment_key FROM service_access_grants");
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<AccessGrant>();
        while (await reader.ReadAsync(ct))
            list.Add(new AccessGrant(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return list;
    }

    public async Task PingAsync(CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand("SELECT 1");
        await cmd.ExecuteScalarAsync(ct);
    }
}
