using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ConfigManager.OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;

namespace ConfigManager.EndToEnd.Tests;

/// <summary>
/// Full path: Management API (write) -> Postgres (snapshot + pg_notify) -> Distribution API (LISTEN + SSE)
/// -> ConfigManager OpenFeature provider (local evaluation).
/// </summary>
[Collection(EndToEndCollection.Name)]
public class FlagLifecycleTests(EndToEndFixture fx)
{
    private static readonly TimeSpan PropagationTimeout = TimeSpan.FromSeconds(15);

    private HttpClient Admin => fx.User("admin", admin: true);
    private HttpClient OrdersEditor => fx.User("alice", "orders-editors");
    private HttpClient PaymentsApprover => fx.User("carol", "payments-team");

    private static EvaluationContext Ctx(string user, params (string k, string v)[] attrs)
    {
        var b = EvaluationContext.Builder().SetTargetingKey(user);
        foreach (var (k, v) in attrs) b.Set(k, v);
        return b.Build();
    }

    private static async Task Eventually(Func<Task<bool>> condition, string because)
    {
        var deadline = DateTime.UtcNow + PropagationTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(100);
        }
        Assert.Fail($"Timed out: {because}");
    }

    /// <summary>Builds a change that sets the default variant of an existing flag config.</summary>
    private static async Task<object> DefaultVariantChange(HttpClient client, string ns, string env, string flag, string variant)
    {
        var detail = await client.GetFromJsonAsync<JsonObject>($"/api/namespaces/{ns}/flags/{flag}");
        var config = detail!["configs"]![env]!.DeepClone().AsObject();
        config["defaultVariant"] = variant;
        return new { title = $"{flag} -> {variant}", operations = new[] { new { type = "upsert", flagKey = flag, flag = config } } };
    }

    [Fact]
    public async Task Seeded_flags_are_served_to_the_provider_with_targeting()
    {
        var provider = await fx.StartProviderAsync("orders-service", "orders", "dev");
        try
        {
            var acme = await provider.ResolveBooleanValueAsync("new-checkout", false, Ctx("u1", ("tenant", "acme")));
            Assert.True(acme.Value);
            Assert.Equal(Reason.TargetingMatch, acme.Reason);

            var max = await provider.ResolveIntegerValueAsync("max-items-per-order", 0, Ctx("u1"));
            Assert.Equal(10, max.Value);

            var missing = await provider.ResolveStringValueAsync("does-not-exist", "fallback", Ctx("u1"));
            Assert.Equal("fallback", missing.Value);
            Assert.Equal(ErrorType.FlagNotFound, missing.ErrorType);
        }
        finally { await provider.ShutdownAsync(); }
    }

    [Fact]
    public async Task Direct_publish_in_dev_reaches_running_provider_via_sse()
    {
        var provider = await fx.StartProviderAsync("orders-service", "orders", "dev");
        try
        {
            var body = await DefaultVariantChange(OrdersEditor, "orders", "dev", "checkout-banner", "blackfriday");
            var res = await OrdersEditor.PostAsJsonAsync("/api/namespaces/orders/environments/dev/changes", body);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            await Eventually(async () =>
                (await provider.ResolveStringValueAsync("checkout-banner", "", Ctx("u1"))).Value == "Black Friday: 30% off!",
                "provider should receive the new banner through SSE");
        }
        finally { await provider.ShutdownAsync(); }
    }

    [Fact]
    public async Task Prod_change_request_is_only_live_after_four_eyes_approval_and_rollback_restores_it()
    {
        var provider = await fx.StartProviderAsync("payments-service", "payments", "prod");
        try
        {
            async Task<double> Fee() => (await provider.ResolveDoubleValueAsync("fee-percentage", -1, Ctx("u1"))).Value;
            Assert.Equal(2.5, await Fee());
            var history = await Admin.GetFromJsonAsync<JsonArray>("/api/namespaces/payments/environments/prod/versions");
            var baseline = history![0]!["version"]!.GetValue<long>();

            // carol (approver of payments) proposes -> CR, cannot self-approve
            var body = await DefaultVariantChange(PaymentsApprover, "payments", "prod", "fee-percentage", "promo");
            var res = await PaymentsApprover.PostAsJsonAsync("/api/namespaces/payments/environments/prod/changes", body);
            Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
            var crId = (await res.Content.ReadFromJsonAsync<JsonObject>())!["changeRequest"]!["id"]!.GetValue<string>();

            Assert.Equal(HttpStatusCode.Forbidden, (await PaymentsApprover.PostAsJsonAsync($"/api/change-requests/{crId}/approve", new { })).StatusCode);
            await Task.Delay(500);
            Assert.Equal(2.5, await Fee()); // pending CR is not published

            // a different approver (admin) approves -> applied -> pushed
            Assert.True((await Admin.PostAsJsonAsync($"/api/change-requests/{crId}/approve", new { comment = "ok" })).IsSuccessStatusCode);
            await Eventually(async () => await Fee() == 1.0, "approved CR should reach provider");

            // rollback to the baseline version also needs a CR; carol approves admin's rollback
            res = await Admin.PostAsJsonAsync($"/api/namespaces/payments/environments/prod/versions/{baseline}/rollback", new { reason = "e2e" });
            Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
            crId = (await res.Content.ReadFromJsonAsync<JsonObject>())!["changeRequest"]!["id"]!.GetValue<string>();
            Assert.True((await PaymentsApprover.PostAsJsonAsync($"/api/change-requests/{crId}/approve", new { })).IsSuccessStatusCode);
            await Eventually(async () => await Fee() == 2.5, "rollback should reach provider");
        }
        finally { await provider.ShutdownAsync(); }
    }

    [Fact]
    public async Task Service_access_is_enforced_and_grants_take_effect_without_restart()
    {
        var svc = fx.Service("reporting-service");
        Assert.Equal(HttpStatusCode.Forbidden, (await svc.GetAsync("/api/v1/snapshots/orders/dev")).StatusCode);

        var grant = await Admin.PostAsJsonAsync("/api/namespaces/orders/service-access", new { clientId = "reporting-service", environment = "dev" });
        Assert.True(grant.IsSuccessStatusCode);

        await Eventually(async () => (await svc.GetAsync("/api/v1/snapshots/orders/dev")).StatusCode == HttpStatusCode.OK,
            "access_changed notification should refresh grants");
        Assert.Equal(HttpStatusCode.Forbidden, (await svc.GetAsync("/api/v1/snapshots/orders/prod")).StatusCode);
    }

    [Fact]
    public async Task Ofrep_remote_evaluation_matches_local_provider_evaluation()
    {
        var provider = await fx.StartProviderAsync("orders-service", "orders", "dev");
        try
        {
            var svc = fx.Service("orders-service");
            foreach (var user in Enumerable.Range(0, 20).Select(i => $"user-{i}"))
            {
                var remote = await (await svc.PostAsJsonAsync("/ns/orders/env/dev/ofrep/v1/evaluate/flags/new-checkout",
                    new { context = new { targetingKey = user, tenant = "other" } })).Content.ReadFromJsonAsync<JsonObject>();
                var local = await provider.ResolveBooleanValueAsync("new-checkout", false, Ctx(user, ("tenant", "other")));

                Assert.Equal(local.Value, remote!["value"]!.GetValue<bool>());
                Assert.Equal(local.Variant, remote["variant"]!.GetValue<string>());
            }
        }
        finally { await provider.ShutdownAsync(); }
    }
}
