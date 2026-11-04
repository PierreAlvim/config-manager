using System.Text.Json;
using ConfigManager.Contracts;
using ConfigManager.Management.Api.Data;
using ConfigManager.Management.Api.Domain;
using ConfigManager.Management.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ConfigManager.Management.Api.Startup;

/// <summary>Demo data described in docs/ARCHITECTURE.md; only applied to an empty database.</summary>
public sealed class SeedData(AppDbContext db, PublishingService publishing)
{
    private const string Actor = "system";

    public async Task SeedIfEmptyAsync(CancellationToken ct)
    {
        if (await db.Environments.AnyAsync(ct) || await db.Namespaces.AnyAsync(ct)) return;

        var now = DateTimeOffset.UtcNow;
        string[] envs = ["dev", "hml", "prod"];
        db.Environments.AddRange(envs.Select((e, i) => new EnvironmentEntity { Key = e, Name = e, SortOrder = i }));
        db.Namespaces.AddRange(
            new NamespaceEntity { Key = "orders", Name = "Orders", Description = "Order management service", CreatedAt = now },
            new NamespaceEntity { Key = "payments", Name = "Payments", Description = "Payments service", CreatedAt = now });
        foreach (var ns in new[] { "orders", "payments" })
            foreach (var env in envs)
                db.Policies.Add(new NamespacePolicy { NamespaceKey = ns, EnvironmentKey = env, RequireChangeRequest = env == "prod", RequiredApprovals = 1 });

        void Bind(string ns, string type, string subject, NamespaceRole role) =>
            db.RoleBindings.Add(new RoleBinding { Id = Guid.NewGuid(), NamespaceKey = ns, SubjectType = type, Subject = subject, Role = role });
        Bind("orders", SubjectTypes.Group, "orders-editors", NamespaceRole.Editor);
        Bind("orders", SubjectTypes.Group, "orders-approvers", NamespaceRole.Approver);
        Bind("orders", SubjectTypes.Group, "viewers", NamespaceRole.Viewer);
        Bind("payments", SubjectTypes.Group, "payments-team", NamespaceRole.Approver);
        Bind("payments", SubjectTypes.User, "alice", NamespaceRole.Viewer);
        Bind("payments", SubjectTypes.Group, "viewers", NamespaceRole.Viewer);

        void Grant(string client, string ns) =>
            db.AccessGrants.Add(new ServiceAccessGrant { Id = Guid.NewGuid(), ClientId = client, NamespaceKey = ns, EnvironmentKey = "*", CreatedAt = now, CreatedBy = Actor });
        Grant("orders-service", "orders");
        Grant("payments-service", "payments");

        var orders = OrdersFlags();
        var payments = PaymentsFlags();
        foreach (var (key, (type, _)) in orders)
            db.Flags.Add(new FlagEntity { NamespaceKey = "orders", Key = key, Name = key, Description = "", Type = type.Type, CreatedAt = now });
        foreach (var (key, (type, _)) in payments)
            db.Flags.Add(new FlagEntity { NamespaceKey = "payments", Key = key, Name = key, Description = "", Type = type.Type, CreatedAt = now });
        db.AddAudit(Actor, "seed.completed");
        await db.SaveChangesAsync(ct);

        foreach (var (ns, flags) in new[] { ("orders", orders), ("payments", payments) })
            foreach (var env in envs)
            {
                var ops = flags.Select(f => ChangeOperation.Upserting(f.Key, f.Value.Definition)).ToList();
                await publishing.PublishAsync(ns, env, ops, Actor, "Initial seed", null, "seed.published", ct);
            }
        await publishing.NotifyAccessChangedAsync(ct);
    }

    private static JsonElement J<T>(T value) => JsonSerializer.SerializeToElement(value, SnapshotJson.Options);

    private static Dictionary<string, (FlagDefinition Definition, int _)> OrdersFlags() => new()
    {
        ["new-checkout"] = (new FlagDefinition
        {
            Type = FlagType.Boolean, Enabled = true,
            Variants = new() { ["on"] = J(true), ["off"] = J(false) },
            DefaultVariant = "off",
            Rules =
            [
                new TargetingRule
                {
                    Name = "beta tenants",
                    Conditions = [new Condition { Attribute = "tenant", Operator = ConditionOperator.In, Values = ["acme"] }],
                    Variant = "on",
                },
                new TargetingRule
                {
                    Name = "10% rollout",
                    Rollout = [new WeightedVariant { Variant = "on", Weight = 10 }, new WeightedVariant { Variant = "off", Weight = 90 }],
                },
            ],
        }, 0),
        ["max-items-per-order"] = (new FlagDefinition
        {
            Type = FlagType.Integer, Variants = new() { ["small"] = J(10), ["large"] = J(50) }, DefaultVariant = "small",
        }, 0),
        ["checkout-banner"] = (new FlagDefinition
        {
            Type = FlagType.String, Variants = new() { ["none"] = J(""), ["blackfriday"] = J("Black Friday: 30% off!") }, DefaultVariant = "none",
        }, 0),
    };

    private static Dictionary<string, (FlagDefinition Definition, int _)> PaymentsFlags() => new()
    {
        ["pix-enabled"] = (new FlagDefinition
        {
            Type = FlagType.Boolean, Variants = new() { ["on"] = J(true), ["off"] = J(false) }, DefaultVariant = "off",
            Rules =
            [
                new TargetingRule
                {
                    Name = "Brazil",
                    Conditions = [new Condition { Attribute = "country", Operator = ConditionOperator.Equals, Values = ["BR"] }],
                    Variant = "on",
                },
            ],
        }, 0),
        ["fee-percentage"] = (new FlagDefinition
        {
            Type = FlagType.Double, Variants = new() { ["standard"] = J(2.5), ["promo"] = J(1.0) }, DefaultVariant = "standard",
        }, 0),
        ["payment-providers"] = (new FlagDefinition
        {
            Type = FlagType.Object,
            Variants = new()
            {
                ["default"] = J(new Dictionary<string, string> { ["primary"] = "stripe", ["fallback"] = "adyen" }),
                ["swapped"] = J(new Dictionary<string, string> { ["primary"] = "adyen", ["fallback"] = "stripe" }),
            },
            DefaultVariant = "default",
        }, 0),
    };
}
