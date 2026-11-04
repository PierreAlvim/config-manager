using System.Text.Json;
using ConfigManager.Contracts;
using ConfigManager.Management.Api.Common;
using ConfigManager.Management.Api.Data;
using ConfigManager.Management.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ConfigManager.Management.Api.Services;

/// <summary>Reads configs/snapshots and publishes new versions transactionally.</summary>
public sealed class PublishingService(AppDbContext db)
{
    public async Task EnsureEnvironmentAsync(string env, CancellationToken ct)
    {
        if (!await db.Environments.AnyAsync(e => e.Key == env, ct))
            throw ApiException.NotFound($"Environment '{env}' does not exist.");
    }

    public async Task<Dictionary<string, FlagType>> GetFlagTypesAsync(string ns, CancellationToken ct) =>
        await db.Flags.AsNoTracking().Where(f => f.NamespaceKey == ns).ToDictionaryAsync(f => f.Key, f => f.Type, ct);

    public async Task ValidateAsync(string ns, IReadOnlyList<ChangeOperation> ops, CancellationToken ct)
    {
        var errors = ChangeValidator.Validate(ops, await GetFlagTypesAsync(ns, ct));
        if (errors.Count > 0) throw ApiException.Validation(errors);
    }

    public async Task<long> LatestVersionAsync(string ns, string env, CancellationToken ct) =>
        await db.PublishedSnapshots.AsNoTracking()
            .Where(s => s.NamespaceKey == ns && s.EnvironmentKey == env)
            .MaxAsync(s => (long?)s.Version, ct) ?? 0;

    /// <summary>Live flag configs of an environment (what the next snapshot is built from).</summary>
    public async Task<Dictionary<string, FlagDefinition>> GetCurrentFlagsAsync(string ns, string env, CancellationToken ct)
    {
        var rows = await db.FlagConfigs.AsNoTracking().Where(c => c.NamespaceKey == ns && c.EnvironmentKey == env).ToListAsync(ct);
        return rows.ToDictionary(r => r.FlagKey, r => SnapshotJson.DeserializeFlag(r.Content), StringComparer.Ordinal);
    }

    public async Task<PublishedSnapshot?> GetSnapshotRowAsync(string ns, string env, long version, CancellationToken ct) =>
        await db.PublishedSnapshots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.NamespaceKey == ns && s.EnvironmentKey == env && s.Version == version, ct);

    public async Task<Dictionary<string, FlagDefinition>> GetFlagsAtVersionAsync(string ns, string env, long version, CancellationToken ct)
    {
        if (version <= 0) return new(StringComparer.Ordinal);
        var row = await GetSnapshotRowAsync(ns, env, version, ct);
        return row is null ? new(StringComparer.Ordinal) : SnapshotJson.Deserialize(row.Content).Flags;
    }

    public async Task<ConfigSnapshot> GetLatestSnapshotAsync(string ns, string env, CancellationToken ct)
    {
        var row = await db.PublishedSnapshots.AsNoTracking()
            .Where(s => s.NamespaceKey == ns && s.EnvironmentKey == env)
            .OrderByDescending(s => s.Version).FirstOrDefaultAsync(ct);
        return row is null
            ? SnapshotBuilder.Build(ns, env, 0, DateTimeOffset.UnixEpoch, new Dictionary<string, FlagDefinition>())
            : SnapshotJson.Deserialize(row.Content);
    }

    /// <summary>
    /// Runs <paramref name="work"/> inside one transaction holding an advisory lock for (ns, env),
    /// so versions are never duplicated under concurrency. Saves and commits on success.
    /// </summary>
    public async Task<T> RunLockedAsync<T>(string ns, string env, Func<Task<T>> work, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lockKey = $"{ns}/{env}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", ct);
        var result = await work();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    /// <summary>Applies operations and publishes the next version. Must run inside <see cref="RunLockedAsync{T}"/>.</summary>
    public async Task<long> ApplyAsync(
        string ns, string env, IReadOnlyList<ChangeOperation> ops, string actor, string? message, Guid? changeRequestId,
        string auditAction, CancellationToken ct)
    {
        await ValidateAsync(ns, ops, ct);
        var now = DateTimeOffset.UtcNow;

        var rows = await db.FlagConfigs.Where(c => c.NamespaceKey == ns && c.EnvironmentKey == env).ToListAsync(ct);
        var current = rows.ToDictionary(r => r.FlagKey, r => SnapshotJson.DeserializeFlag(r.Content), StringComparer.Ordinal);
        var next = ChangePlanner.Apply(current, ops);

        foreach (var op in ops)
        {
            var row = rows.FirstOrDefault(r => r.FlagKey == op.FlagKey);
            if (op.Type == ChangeOperation.Delete)
            {
                if (row is not null) db.FlagConfigs.Remove(row);
            }
            else if (row is null)
            {
                db.FlagConfigs.Add(new FlagEnvironmentConfig
                {
                    NamespaceKey = ns, EnvironmentKey = env, FlagKey = op.FlagKey,
                    Content = SnapshotJson.Serialize(op.Flag!), UpdatedAt = now,
                });
            }
            else
            {
                row.Content = SnapshotJson.Serialize(op.Flag!);
                row.UpdatedAt = now;
            }
        }

        var version = await LatestVersionAsync(ns, env, ct) + 1;
        var snapshot = SnapshotBuilder.Build(ns, env, version, now, next);
        db.PublishedSnapshots.Add(new PublishedSnapshot
        {
            Id = Guid.NewGuid(), NamespaceKey = ns, EnvironmentKey = env, Version = version,
            Content = SnapshotJson.Serialize(snapshot), PublishedAt = now, PublishedBy = actor,
            ChangeRequestId = changeRequestId, Message = message,
        });
        await db.SaveChangesAsync(ct);

        var payload = $"{ns}/{env}/{version}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_notify('config_published', {payload})", ct);

        db.AddAudit(actor, auditAction, ns, env,
            JsonSerializer.Serialize(new { version, message, changeRequestId, flags = ops.Select(o => $"{o.Type}:{o.FlagKey}") }));
        return version;
    }

    /// <summary>Convenience: lock + apply + commit.</summary>
    public Task<long> PublishAsync(
        string ns, string env, IReadOnlyList<ChangeOperation> ops, string actor, string? message, Guid? changeRequestId,
        string auditAction, CancellationToken ct) =>
        RunLockedAsync(ns, env, () => ApplyAsync(ns, env, ops, actor, message, changeRequestId, auditAction, ct), ct);

    public Task NotifyAccessChangedAsync(CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_notify('access_changed', '')", ct);
}
