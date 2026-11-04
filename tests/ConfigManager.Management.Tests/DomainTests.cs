using System.Text.Json;
using ConfigManager.Contracts;
using ConfigManager.Management.Api.Domain;

namespace ConfigManager.Management.Tests;

public class ChangeValidatorTests
{
    private static readonly Dictionary<string, FlagType> Types = new() { ["f-bool"] = FlagType.Boolean, ["f-str"] = FlagType.String };

    [Fact]
    public void Valid_upsert_and_delete_pass() =>
        Assert.Empty(ChangeValidator.Validate([ChangeOperation.Upserting("f-bool", TestFlags.Bool()), ChangeOperation.Deleting("f-str")], Types));

    [Fact]
    public void Empty_operations_rejected() =>
        Assert.NotEmpty(ChangeValidator.Validate([], Types));

    [Fact]
    public void Type_mismatch_rejected()
    {
        var errors = ChangeValidator.Validate([ChangeOperation.Upserting("f-bool", TestFlags.Str())], Types);
        Assert.Contains(errors, e => e.Contains("does not match"));
    }

    [Fact]
    public void Unknown_flag_rejected()
    {
        var errors = ChangeValidator.Validate([ChangeOperation.Deleting("nope")], Types);
        Assert.Contains(errors, e => e.Contains("does not exist"));
    }

    [Fact]
    public void Invalid_key_rejected()
    {
        var errors = ChangeValidator.Validate([ChangeOperation.Deleting("Bad Key")], Types);
        Assert.Contains(errors, e => e.Contains("Invalid flag key"));
    }

    [Fact]
    public void Duplicate_flag_rejected()
    {
        var errors = ChangeValidator.Validate([ChangeOperation.Deleting("f-str"), ChangeOperation.Deleting("f-str")], Types);
        Assert.Contains(errors, e => e.Contains("more than one"));
    }

    [Fact]
    public void Unknown_operation_type_rejected()
    {
        var errors = ChangeValidator.Validate([new ChangeOperation { Type = "merge", FlagKey = "f-str" }], Types);
        Assert.Contains(errors, e => e.Contains("Unknown operation"));
    }

    [Fact]
    public void Invalid_definition_reports_flag_validator_errors()
    {
        var bad = TestFlags.Bool(defaultVariant: "missing");
        var errors = ChangeValidator.Validate([ChangeOperation.Upserting("f-bool", bad)], Types);
        Assert.Contains(errors, e => e.Contains("Default variant"));
    }

    [Fact]
    public void Upsert_without_definition_rejected()
    {
        var errors = ChangeValidator.Validate([new ChangeOperation { Type = "upsert", FlagKey = "f-bool" }], Types);
        Assert.NotEmpty(errors);
    }
}

public class ChangeRequestWorkflowTests
{
    private static ReviewCheck Check(ChangeRequestStatus s = ChangeRequestStatus.Pending, string author = "alice", string reviewer = "bob",
        NamespaceRole? role = NamespaceRole.Approver, params string[] prior) =>
        ChangeRequestWorkflow.CheckCanReview(s, author, reviewer, role, prior);

    [Fact] public void Approver_can_review() => Assert.Equal(ReviewCheck.Ok, Check());
    [Fact] public void Author_cannot_review_own() => Assert.Equal(ReviewCheck.IsAuthor, Check(reviewer: "alice", role: NamespaceRole.Owner));
    [Fact] public void Editor_cannot_review() => Assert.Equal(ReviewCheck.InsufficientRole, Check(role: NamespaceRole.Editor));
    [Fact] public void No_role_cannot_review() => Assert.Equal(ReviewCheck.InsufficientRole, Check(role: null));
    [Fact] public void Only_pending_can_be_reviewed() => Assert.Equal(ReviewCheck.NotPending, Check(ChangeRequestStatus.Applied));
    [Fact] public void Cannot_review_twice() => Assert.Equal(ReviewCheck.AlreadyReviewed, Check(prior: "bob"));

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(1, 2, false)]
    [InlineData(2, 2, true)]
    [InlineData(0, 0, false)]
    public void Counts_approvals(int approvals, int required, bool expected) =>
        Assert.Equal(expected, ChangeRequestWorkflow.HasEnoughApprovals(approvals, required));

    [Fact] public void Author_can_cancel_pending() => Assert.True(ChangeRequestWorkflow.CanCancel(ChangeRequestStatus.Pending, "alice", "alice", NamespaceRole.Editor));
    [Fact] public void Owner_can_cancel_pending() => Assert.True(ChangeRequestWorkflow.CanCancel(ChangeRequestStatus.Pending, "alice", "root", NamespaceRole.Owner));
    [Fact] public void Other_editor_cannot_cancel() => Assert.False(ChangeRequestWorkflow.CanCancel(ChangeRequestStatus.Pending, "alice", "bob", NamespaceRole.Approver));
    [Fact] public void Cannot_cancel_applied() => Assert.False(ChangeRequestWorkflow.CanCancel(ChangeRequestStatus.Applied, "alice", "alice", NamespaceRole.Owner));

    [Fact]
    public void Detects_conflict_when_touched_flag_changed_since_base()
    {
        var atBase = new Dictionary<string, FlagDefinition> { ["a"] = TestFlags.Bool(), ["b"] = TestFlags.Bool() };
        var current = new Dictionary<string, FlagDefinition> { ["a"] = TestFlags.Bool(enabled: false), ["b"] = TestFlags.Bool() };
        var conflicts = ChangeRequestWorkflow.FindConflicts(atBase, current, [ChangeOperation.Upserting("a", TestFlags.Bool(defaultVariant: "on")), ChangeOperation.Deleting("b")]);
        Assert.Equal(["a"], conflicts);
    }

    [Fact]
    public void No_conflict_when_other_flags_changed()
    {
        var atBase = new Dictionary<string, FlagDefinition> { ["a"] = TestFlags.Bool() };
        var current = new Dictionary<string, FlagDefinition> { ["a"] = TestFlags.Bool(), ["z"] = TestFlags.Bool() };
        Assert.Empty(ChangeRequestWorkflow.FindConflicts(atBase, current, [ChangeOperation.Upserting("a", TestFlags.Bool(enabled: false))]));
    }

    [Fact]
    public void Conflict_when_flag_created_concurrently()
    {
        var current = new Dictionary<string, FlagDefinition> { ["a"] = TestFlags.Bool() };
        Assert.Single(ChangeRequestWorkflow.FindConflicts(new Dictionary<string, FlagDefinition>(), current, [ChangeOperation.Upserting("a", TestFlags.Bool())]));
    }
}

public class SnapshotAndPlannerTests
{
    [Fact]
    public void Builds_snapshot_with_all_flags_and_version()
    {
        var flags = new Dictionary<string, FlagDefinition> { ["b"] = TestFlags.Bool(), ["a"] = TestFlags.Str() };
        var at = DateTimeOffset.UtcNow;
        var snap = SnapshotBuilder.Build("orders", "dev", 4, at, flags);
        Assert.Equal(("orders", "dev", 4L, at), (snap.Namespace, snap.Environment, snap.Version, snap.PublishedAt));
        Assert.Equal(["a", "b"], snap.Flags.Keys);
    }

    [Fact]
    public void Snapshot_roundtrips_through_contract_json()
    {
        var snap = SnapshotBuilder.Build("o", "d", 1, DateTimeOffset.UtcNow, new Dictionary<string, FlagDefinition> { ["f"] = TestFlags.Bool() });
        var back = SnapshotJson.Deserialize(SnapshotJson.Serialize(snap));
        Assert.True(ChangePlanner.AreEqual(snap.Flags["f"], back.Flags["f"]));
    }

    [Fact]
    public void Apply_upserts_and_deletes()
    {
        var current = new Dictionary<string, FlagDefinition> { ["a"] = TestFlags.Bool(), ["b"] = TestFlags.Bool() };
        var next = ChangePlanner.Apply(current, [ChangeOperation.Deleting("a"), ChangeOperation.Upserting("c", TestFlags.Str())]);
        Assert.Equal(["b", "c"], next.Keys.Order());
        Assert.Equal(["a", "b"], current.Keys.Order()); // input untouched
    }

    [Fact]
    public void Rollback_restores_target_flag_set()
    {
        var target = new Dictionary<string, FlagDefinition> { ["keep"] = TestFlags.Bool(), ["changed"] = TestFlags.Bool(), ["gone"] = TestFlags.Bool() };
        var current = new Dictionary<string, FlagDefinition> { ["keep"] = TestFlags.Bool(), ["changed"] = TestFlags.Bool(enabled: false), ["added"] = TestFlags.Bool() };
        var ops = ChangePlanner.Rollback(target, current);

        Assert.Equal(["changed:upsert", "gone:upsert", "added:delete"], ops.Select(o => $"{o.FlagKey}:{o.Type}"));
        var result = ChangePlanner.Apply(current, ops);
        Assert.Equal(target.Keys.Order(), result.Keys.Order());
        Assert.All(target, kv => Assert.True(ChangePlanner.AreEqual(kv.Value, result[kv.Key])));
    }

    [Fact]
    public void Rollback_to_identical_state_yields_no_operations()
    {
        var flags = new Dictionary<string, FlagDefinition> { ["a"] = TestFlags.Bool() };
        Assert.Empty(ChangePlanner.Rollback(flags, new Dictionary<string, FlagDefinition>(flags)));
    }

    [Fact]
    public void Promote_all_copies_only_differences()
    {
        var source = new Dictionary<string, FlagDefinition> { ["same"] = TestFlags.Bool(), ["diff"] = TestFlags.Bool(enabled: false), ["new"] = TestFlags.Str() };
        var target = new Dictionary<string, FlagDefinition> { ["same"] = TestFlags.Bool(), ["diff"] = TestFlags.Bool(), ["extra"] = TestFlags.Bool() };
        var plan = ChangePlanner.Promote(source, target, null);
        Assert.Equal(["diff", "new"], plan.Operations.Select(o => o.FlagKey));
        Assert.All(plan.Operations, o => Assert.Equal("upsert", o.Type));
        Assert.Empty(plan.MissingInSource);
    }

    [Fact]
    public void Promote_subset_and_reports_missing()
    {
        var source = new Dictionary<string, FlagDefinition> { ["a"] = TestFlags.Bool(), ["b"] = TestFlags.Bool() };
        var plan = ChangePlanner.Promote(source, new Dictionary<string, FlagDefinition>(), ["a", "zzz"]);
        Assert.Equal(["a"], plan.Operations.Select(o => o.FlagKey));
        Assert.Equal(["zzz"], plan.MissingInSource);
    }

    [Fact]
    public void FlagEquality_ignores_variant_order()
    {
        var a = TestFlags.Bool();
        var b = a with { Variants = new() { ["off"] = JsonSerializer.SerializeToElement(false), ["on"] = JsonSerializer.SerializeToElement(true) } };
        Assert.True(ChangePlanner.AreEqual(a, b));
        Assert.False(ChangePlanner.AreEqual(a, null));
    }
}
