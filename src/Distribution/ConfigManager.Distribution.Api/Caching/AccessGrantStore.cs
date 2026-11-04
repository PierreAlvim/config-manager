using ConfigManager.Distribution.Api.Data;

namespace ConfigManager.Distribution.Api.Caching;

/// <summary>Immutable set of grants. Environment <c>*</c> grants every environment of a namespace.</summary>
public sealed class AccessGrants(IEnumerable<AccessGrant> grants)
{
    public const string Wildcard = "*";

    private readonly HashSet<(string, string, string)> _set =
        grants.Select(g => (g.ClientId, g.Namespace, g.Environment)).ToHashSet();

    public bool IsAllowed(string clientId, string ns, string env) =>
        _set.Contains((clientId, ns, env)) || _set.Contains((clientId, ns, Wildcard));
}

/// <summary>Cache of <c>service_access_grants</c>; loaded on first use, replaced atomically on reload.</summary>
public sealed class AccessGrantStore(ISnapshotSource source)
{
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private volatile AccessGrants? _grants;

    public async Task<bool> IsAllowedAsync(string clientId, string ns, string env, CancellationToken ct)
    {
        var grants = _grants;
        if (grants is null)
        {
            await _loadLock.WaitAsync(ct);
            try
            {
                grants = _grants ??= new AccessGrants(await source.GetGrantsAsync(ct));
            }
            finally
            {
                _loadLock.Release();
            }
        }
        return grants.IsAllowed(clientId, ns, env);
    }

    public async Task ReloadAsync(CancellationToken ct) =>
        _grants = new AccessGrants(await source.GetGrantsAsync(ct));
}
