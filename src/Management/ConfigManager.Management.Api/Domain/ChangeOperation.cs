using System.Text.Json;
using System.Text.Json.Nodes;
using ConfigManager.Contracts;

namespace ConfigManager.Management.Api.Domain;

public sealed record ChangeOperation
{
    public const string Upsert = "upsert";
    public const string Delete = "delete";

    public required string Type { get; init; }
    public required string FlagKey { get; init; }
    public FlagDefinition? Flag { get; init; }

    public static ChangeOperation Upserting(string key, FlagDefinition flag) => new() { Type = Upsert, FlagKey = key, Flag = flag };
    public static ChangeOperation Deleting(string key) => new() { Type = Delete, FlagKey = key };
}

public static class ChangeValidator
{
    /// <summary>Validates operations against the flag metadata (key -> type) of the namespace.</summary>
    public static List<string> Validate(IReadOnlyList<ChangeOperation> ops, IReadOnlyDictionary<string, FlagType> flagTypes)
    {
        var errors = new List<string>();
        if (ops.Count == 0)
        {
            errors.Add("At least one operation is required.");
            return errors;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in ops)
        {
            if (op.Type is not (ChangeOperation.Upsert or ChangeOperation.Delete))
            {
                errors.Add($"Unknown operation type '{op.Type}'.");
                continue;
            }
            if (!KeyRules.IsValidKey(op.FlagKey))
            {
                errors.Add($"Invalid flag key '{op.FlagKey}'.");
                continue;
            }
            if (!seen.Add(op.FlagKey))
                errors.Add($"Flag '{op.FlagKey}' appears in more than one operation.");
            if (!flagTypes.TryGetValue(op.FlagKey, out var type))
            {
                errors.Add($"Flag '{op.FlagKey}' does not exist in this namespace.");
                continue;
            }
            if (op.Type == ChangeOperation.Delete) continue;

            if (op.Flag is null)
            {
                errors.Add($"Flag '{op.FlagKey}': 'flag' is required for upsert.");
                continue;
            }
            if (op.Flag.Type != type)
                errors.Add($"Flag '{op.FlagKey}': type '{op.Flag.Type.ToString().ToLowerInvariant()}' does not match the flag's type '{type.ToString().ToLowerInvariant()}'.");
            errors.AddRange(FlagValidator.Validate(op.FlagKey, op.Flag).Select(e => $"Flag '{op.FlagKey}': {e}"));
        }
        return errors;
    }
}

public sealed record PromotionPlan(List<ChangeOperation> Operations, List<string> MissingInSource);

public static class ChangePlanner
{
    public static bool AreEqual(FlagDefinition? a, FlagDefinition? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return JsonNode.DeepEquals(
            JsonSerializer.SerializeToNode(a, SnapshotJson.Options),
            JsonSerializer.SerializeToNode(b, SnapshotJson.Options));
    }

    public static Dictionary<string, FlagDefinition> Apply(IReadOnlyDictionary<string, FlagDefinition> current, IEnumerable<ChangeOperation> ops)
    {
        var next = new Dictionary<string, FlagDefinition>(current, StringComparer.Ordinal);
        foreach (var op in ops)
        {
            if (op.Type == ChangeOperation.Delete) next.Remove(op.FlagKey);
            else next[op.FlagKey] = op.Flag ?? throw new InvalidOperationException($"Upsert of '{op.FlagKey}' without definition.");
        }
        return next;
    }

    /// <summary>Operations that turn <paramref name="current"/> into the flag set of <paramref name="target"/> (only differences).</summary>
    public static List<ChangeOperation> Rollback(IReadOnlyDictionary<string, FlagDefinition> target, IReadOnlyDictionary<string, FlagDefinition> current)
    {
        var ops = new List<ChangeOperation>();
        foreach (var (key, flag) in target.OrderBy(k => k.Key, StringComparer.Ordinal))
            if (!current.TryGetValue(key, out var cur) || !AreEqual(cur, flag))
                ops.Add(ChangeOperation.Upserting(key, flag));
        foreach (var key in current.Keys.Where(k => !target.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal))
            ops.Add(ChangeOperation.Deleting(key));
        return ops;
    }

    /// <summary>Upserts copying flags from source env to target env (all, or the selected keys); identical configs are skipped.</summary>
    public static PromotionPlan Promote(IReadOnlyDictionary<string, FlagDefinition> source, IReadOnlyDictionary<string, FlagDefinition> target, IReadOnlyCollection<string>? flagKeys)
    {
        var keys = flagKeys is { Count: > 0 } ? flagKeys.Distinct().ToList() : source.Keys.ToList();
        var missing = keys.Where(k => !source.ContainsKey(k)).ToList();
        var ops = keys.Where(source.ContainsKey).OrderBy(k => k, StringComparer.Ordinal)
            .Where(k => !target.TryGetValue(k, out var cur) || !AreEqual(cur, source[k]))
            .Select(k => ChangeOperation.Upserting(k, source[k]))
            .ToList();
        return new PromotionPlan(ops, missing);
    }
}

public static class SnapshotBuilder
{
    public static ConfigSnapshot Build(string ns, string env, long version, DateTimeOffset publishedAt, IReadOnlyDictionary<string, FlagDefinition> flags) => new()
    {
        Namespace = ns,
        Environment = env,
        Version = version,
        PublishedAt = publishedAt,
        Flags = flags.OrderBy(f => f.Key, StringComparer.Ordinal).ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal),
    };
}

public enum ReviewCheck { Ok, NotPending, InsufficientRole, IsAuthor, AlreadyReviewed }

/// <summary>Pure change-request state machine rules.</summary>
public static class ChangeRequestWorkflow
{
    public static ReviewCheck CheckCanReview(ChangeRequestStatus status, string author, string reviewer, NamespaceRole? role, IEnumerable<string> priorReviewers)
    {
        if (status != ChangeRequestStatus.Pending) return ReviewCheck.NotPending;
        if (role is null || role < NamespaceRole.Approver) return ReviewCheck.InsufficientRole;
        if (string.Equals(author, reviewer, StringComparison.Ordinal)) return ReviewCheck.IsAuthor;
        if (priorReviewers.Contains(reviewer, StringComparer.Ordinal)) return ReviewCheck.AlreadyReviewed;
        return ReviewCheck.Ok;
    }

    public static bool HasEnoughApprovals(int approvals, int required) => approvals >= Math.Max(1, required);

    public static bool CanCancel(ChangeRequestStatus status, string author, string user, NamespaceRole? role) =>
        status == ChangeRequestStatus.Pending
        && (string.Equals(author, user, StringComparison.Ordinal) || role >= NamespaceRole.Owner);

    /// <summary>Keys touched by the operations whose current config differs from the config at the CR's base version.</summary>
    public static List<string> FindConflicts(
        IReadOnlyDictionary<string, FlagDefinition> atBase,
        IReadOnlyDictionary<string, FlagDefinition> current,
        IEnumerable<ChangeOperation> ops)
    {
        var conflicts = new List<string>();
        foreach (var key in ops.Select(o => o.FlagKey).Distinct())
        {
            atBase.TryGetValue(key, out var b);
            current.TryGetValue(key, out var c);
            if (!ChangePlanner.AreEqual(b, c)) conflicts.Add(key);
        }
        return conflicts;
    }
}
