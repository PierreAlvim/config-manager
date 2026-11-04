using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ConfigManager.Contracts;
using Npgsql;

namespace ConfigManager.Management.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ManagementApiTests(ApiFixture api)
{
    private static string BaseUrl(string ns, string env) => $"/api/namespaces/{ns}/environments/{env}";

    [Fact]
    public async Task Health_endpoints_are_anonymous()
    {
        var c = api.ClientFor(null);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Seed_creates_version_1_snapshots_for_every_namespace_and_environment()
    {
        var rows = await api.QueryAsync(
            "SELECT namespace_key, environment_key, content::text FROM published_snapshots WHERE version = 1 AND namespace_key IN ('orders','payments')",
            r => (Ns: r.GetString(0), Env: r.GetString(1), Content: r.GetString(2)));
        Assert.Equal(6, rows.Count);
        foreach (var row in rows)
        {
            var snap = SnapshotJson.Deserialize(row.Content);
            Assert.Equal(1, snap.Version);
            Assert.Equal(row.Ns, snap.Namespace);
            Assert.Equal(3, snap.Flags.Count);
        }
        var orders = SnapshotJson.Deserialize(rows.First(r => r.Ns == "orders" && r.Env == "dev").Content);
        Assert.Equal(2, orders.Flags["new-checkout"].Rules.Count);
        Assert.Equal(FlagType.Integer, orders.Flags["max-items-per-order"].Type);

        var grants = await api.QueryAsync("SELECT client_id, namespace_key, environment_key FROM service_access_grants WHERE created_by = 'system'",
            r => $"{r.GetString(0)}:{r.GetString(1)}:{r.GetString(2)}");
        Assert.Contains("orders-service:orders:*", grants);
        Assert.Contains("payments-service:payments:*", grants);
    }

    [Fact]
    public async Task Me_reports_groups_roles_and_admin()
    {
        var me = await (await api.ClientFor("alice", "orders-editors").GetAsync("/api/me")).Json();
        Assert.Equal("alice", me.GetProperty("username").GetString());
        Assert.Equal("editor", me.GetProperty("namespaceRoles").GetProperty("orders").GetString());
        Assert.Equal("viewer", me.GetProperty("namespaceRoles").GetProperty("payments").GetString());
        Assert.False(me.GetProperty("isAdmin").GetBoolean());

        var admin = await (await api.ClientFor("admin", admin: true).GetAsync("/api/me")).Json();
        Assert.True(admin.GetProperty("isAdmin").GetBoolean());
        Assert.Equal("owner", admin.GetProperty("namespaceRoles").GetProperty("orders").GetString());
    }

    [Fact]
    public async Task Non_member_does_not_see_namespace_and_gets_403()
    {
        var s = await Scenario.CreateAsync(api);
        var outsider = api.ClientFor("nobody", "some-group");
        var list = await (await outsider.GetAsync("/api/namespaces")).Json();
        Assert.DoesNotContain(list.EnumerateArray(), n => n.GetProperty("key").GetString() == s.Ns);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"/api/namespaces/{s.Ns}/flags")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync("/api/namespaces/does-not-exist/flags")).StatusCode);
    }

    [Fact]
    public async Task Group_binding_grants_access()
    {
        var s = await Scenario.CreateAsync(api);
        (await s.Admin.PostAsJsonAsync($"/api/namespaces/{s.Ns}/members", new { subjectType = "group", subject = "team-" + s.Suffix, role = "editor" })).EnsureSuccessStatusCode();
        var res = await api.ClientFor("someone", $"x,team-{s.Suffix}").GetAsync($"/api/namespaces/{s.Ns}");
        Assert.Equal("editor", (await res.Json()).GetProperty("myRole").GetString());
    }

    [Fact]
    public async Task Direct_apply_in_dev_increments_version_and_publishes_snapshot()
    {
        var s = await Scenario.CreateAsync(api);
        await s.CreateFlagAsync("f1");
        Assert.Equal(1, await s.ApplyDevAsync("f1", Scenario.BoolFlag()));
        Assert.Equal(2, await s.ApplyDevAsync("f1", Scenario.BoolFlag(defaultVariant: "on")));

        var rows = await api.QueryAsync("SELECT version, content::text, published_by FROM published_snapshots WHERE namespace_key = @ns AND environment_key = 'dev' ORDER BY version",
            r => (r.GetInt64(0), r.GetString(1), r.GetString(2)), ("ns", s.Ns));
        Assert.Equal([1L, 2L], rows.Select(r => r.Item1));
        var snap = SnapshotJson.Deserialize(rows[1].Item2);
        Assert.Equal(2, snap.Version);
        Assert.Equal("on", snap.Flags["f1"].DefaultVariant);
        Assert.Equal(s.Editor, rows[1].Item3);

        var live = await (await s.As(s.Viewer).GetAsync(BaseUrl(s.Ns, "dev") + "/config")).Json();
        Assert.Equal(2, live.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task Concurrent_applies_never_duplicate_versions()
    {
        var s = await Scenario.CreateAsync(api);
        await s.CreateFlagAsync("f1");
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => s.ApplyDevAsync("f1", Scenario.BoolFlag(enabled: i % 2 == 0))));
        Assert.Equal(Enumerable.Range(1, 8).Select(i => (long)i), results.Order());
    }

    [Fact]
    public async Task Invalid_changes_are_rejected_with_validation_problem()
    {
        var s = await Scenario.CreateAsync(api);
        await s.CreateFlagAsync("f1");

        var unknown = await s.ChangeAsync(s.Editor, "dev", "t", false, Scenario.Upsert("nope", Scenario.BoolFlag()));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("does not exist", (await unknown.Json()).GetProperty("errors")[0].GetString());

        var wrongType = await s.ChangeAsync(s.Editor, "dev", "t", false, Scenario.Upsert("f1", new
        {
            type = "string", variants = new { a = "x" }, defaultVariant = "a",
        }));
        Assert.Equal(HttpStatusCode.BadRequest, wrongType.StatusCode);

        var badKey = await s.ChangeAsync(s.Editor, "dev", "t", false, Scenario.Upsert("Bad Key", Scenario.BoolFlag()));
        Assert.Equal(HttpStatusCode.BadRequest, badKey.StatusCode);

        var empty = await s.ChangeAsync(s.Editor, "dev", "t");
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await s.ChangeAsync(s.Editor, "nowhere", "t", false, Scenario.Upsert("f1", Scenario.BoolFlag()))).StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_submit_change_or_create_flag()
    {
        var s = await Scenario.CreateAsync(api);
        await s.CreateFlagAsync("f1");
        Assert.Equal(HttpStatusCode.Forbidden, (await s.ChangeAsync(s.Viewer, "dev", "t", false, Scenario.Upsert("f1", Scenario.BoolFlag()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.As(s.Viewer).PostAsJsonAsync($"/api/namespaces/{s.Ns}/flags", new { key = "x", name = "x", type = "boolean" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.As(s.Viewer).GetAsync($"/api/namespaces/{s.Ns}/flags")).StatusCode);
    }

    [Fact]
    public async Task Flag_lifecycle_and_delete_rules()
    {
        var s = await Scenario.CreateAsync(api);
        await s.CreateFlagAsync("f1");
        Assert.Equal(HttpStatusCode.Conflict, (await s.As(s.Editor).PostAsJsonAsync($"/api/namespaces/{s.Ns}/flags", new { key = "f1", name = "dup", type = "boolean" })).StatusCode);
        await s.ApplyDevAsync("f1", Scenario.BoolFlag());

        var list = await (await s.As(s.Viewer).GetAsync($"/api/namespaces/{s.Ns}/flags")).Json();
        var env = list[0].GetProperty("environments");
        Assert.Equal("off", env.GetProperty("dev").GetProperty("defaultVariant").GetString());
        Assert.Equal(JsonValueKind.Null, env.GetProperty("prod").ValueKind);

        var detail = await (await s.As(s.Viewer).GetAsync($"/api/namespaces/{s.Ns}/flags/f1")).Json();
        Assert.Equal("boolean", detail.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Object, detail.GetProperty("configs").GetProperty("dev").ValueKind);

        Assert.Equal(HttpStatusCode.Conflict, (await s.As(s.Editor).DeleteAsync($"/api/namespaces/{s.Ns}/flags/f1")).StatusCode);
        var del = await s.ChangeAsync(s.Editor, "dev", "remove", false, new { type = "delete", flagKey = "f1" });
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await s.As(s.Editor).DeleteAsync($"/api/namespaces/{s.Ns}/flags/f1")).StatusCode);
    }

    [Fact]
    public async Task Prod_requires_change_request_and_author_cannot_approve()
    {
        var s = await Scenario.CreateAsync(api);
        await s.CreateFlagAsync("f1");
        var id = await s.OpenProdCrAsync("f1", Scenario.BoolFlag());

        var self = await s.As(s.Editor).PostAsync($"/api/change-requests/{id}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
        var editorOther = await s.As(s.Editor2).PostAsync($"/api/change-requests/{id}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, editorOther.StatusCode);

        var detail = await (await s.As(s.Editor).GetAsync($"/api/change-requests/{id}")).Json();
        Assert.Equal("pending", detail.GetProperty("status").GetString());
        Assert.False(detail.GetProperty("canApprove").GetBoolean());
        Assert.True(detail.GetProperty("canCancel").GetBoolean());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("diff")[0].GetProperty("before").ValueKind);
        Assert.Equal("off", detail.GetProperty("diff")[0].GetProperty("after").GetProperty("defaultVariant").GetString());

        var asApprover = await (await s.As(s.Approver).GetAsync($"/api/change-requests/{id}")).Json();
        Assert.True(asApprover.GetProperty("canApprove").GetBoolean());
        Assert.False(asApprover.GetProperty("canCancel").GetBoolean());
        Assert.Equal(0, (await api.QueryAsync("SELECT count(*) FROM published_snapshots WHERE namespace_key = @ns", r => r.GetInt64(0), ("ns", s.Ns)))[0]);
    }

    [Fact]
    public async Task Approver_approves_and_change_is_applied_as_new_version()
    {
        var s = await Scenario.CreateAsync(api);
        await s.CreateFlagAsync("f1");
        var id = await s.OpenProdCrAsync("f1", Scenario.BoolFlag());

        var res = await s.As(s.Approver).PostAsJsonAsync($"/api/change-requests/{id}/approve", new { comment = "lgtm" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var detail = await res.Json();
        Assert.Equal("applied", detail.GetProperty("status").GetString());
        Assert.Equal(1, detail.GetProperty("appliedVersion").GetInt64());
        Assert.Equal("approved", detail.GetProperty("reviews")[0].GetProperty("decision").GetString());

        var rows = await api.QueryAsync("SELECT content::text, change_request_id FROM published_snapshots WHERE namespace_key = @ns AND environment_key = 'prod' AND version = 1",
            r => (r.GetString(0), r.GetGuid(1)), ("ns", s.Ns));
        Assert.Single(rows);
        Assert.Contains("f1", SnapshotJson.Deserialize(rows[0].Item1).Flags.Keys);
        Assert.Equal(id, rows[0].Item2);

        // second approval attempt on a finished CR
        Assert.Equal(HttpStatusCode.Conflict, (await s.As(s.Approver2).PostAsync($"/api/change-requests/{id}/approve", null)).StatusCode);
    }

    [Fact]
    public async Task Required_approvals_greater_than_one_need_distinct_approvers()
    {
        var s = await Scenario.CreateAsync(api);
        await s.CreateFlagAsync("f1");
        var put = await s.Admin.PutAsJsonAsync($"/api/namespaces/{s.Ns}/policies/prod", new { requireChangeRequest = true, requiredApprovals = 2 });
        put.EnsureSuccessStatusCode();
        var id = await s.OpenProdCrAsync("f1", Scenario.BoolFlag());

        var first = await (await s.As(s.Approver).PostAsync($"/api/change-requests/{id}/approve", null)).Json();
        Assert.Equal("pending", first.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await s.As(s.Approver).PostAsync($"/api/change-requests/{id}/approve", null)).StatusCode);
        var second = await (await s.As(s.Approver2).PostAsync($"/api/change-requests/{id}/approve", null)).Json();
        Assert.Equal("applied", second.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Reject_and_cancel_rules()
    {
        var s = await Scenario.CreateAsync(api);
        await s.CreateFlagAsync("f1");

        var rejected = await s.OpenProdCrAsync("f1", Scenario.BoolFlag());
        var res = await s.As(s.Approver).PostAsJsonAsync($"/api/change-requests/{rejected}/reject", new { comment = "no" });
        Assert.Equal("rejected", (await res.Json()).GetProperty("status").GetString());

        var cancelled = await s.OpenProdCrAsync("f1", Scenario.BoolFlag());
        Assert.Equal(HttpStatusCode.Forbidden, (await s.As(s.Editor2).PostAsync($"/api/change-requests/{cancelled}/cancel", null)).StatusCode);
        var c = await s.As(s.Editor).PostAsync($"/api/change-requests/{cancelled}/cancel", null);
        Assert.Equal("cancelled", (await c.Json()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await s.As(s.Approver).PostAsync($"/api/change-requests/{cancelled}/approve", null)).StatusCode);

        var byOwner = await s.OpenProdCrAsync("f1", Scenario.BoolFlag());
        Assert.Equal(HttpStatusCode.OK, (await s.Admin.PostAsync($"/api/change-requests/{byOwner}/cancel", null)).StatusCode);

        var list = await (await s.As(s.Viewer).GetAsync($"/api/change-requests?namespace={s.Ns}&status=rejected")).Json();
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal(3, (await (await s.As(s.Viewer).GetAsync($"/api/change-requests?namespace={s.Ns}")).Json()).GetArrayLength());
    }

    [Fact]
    public async Task Force_change_request_in_dev_returns_202()
    {
        var s = await Scenario.CreateAsync(api);
        await s.CreateFlagAsync("f1");
        var res = await s.ChangeAsync(s.Editor, "dev", "forced", true, Scenario.Upsert("f1", Scenario.BoolFlag()));
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        Assert.Equal("changeRequestCreated", (await res.Json()).GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Conflicting_change_request_is_marked_conflict_and_not_applied()
    {
        var s = await Scenario.CreateAsync(api);
        await s.CreateFlagAsync("f1");
        var a = await s.OpenProdCrAsync("f1", Scenario.BoolFlag(enabled: true));
        var b = await s.OpenProdCrAsync("f1", Scenario.BoolFlag(enabled: false), s.Editor2);

        Assert.Equal(HttpStatusCode.OK, (await s.As(s.Approver).PostAsync($"/api/change-requests/{b}/approve", null)).StatusCode);
        var res = await s.As(s.Approver).PostAsync($"/api/change-requests/{a}/approve", null);
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);

        var detail = await (await s.As(s.Approver).GetAsync($"/api/change-requests/{a}")).Json();
        Assert.Equal("conflict", detail.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("appliedVersion").ValueKind);
        var versions = await (await s.As(s.Viewer).GetAsync(BaseUrl(s.Ns, "prod") + "/versions")).Json();
        Assert.Equal(1, versions.GetArrayLength());
    }

    [Fact]
    public async Task Rollback_creates_new_version_equal_to_target()
    {
        var s = await Scenario.CreateAsync(api);
        await s.CreateFlagAsync("f1");
        await s.CreateFlagAsync("f2");
        await s.ApplyDevAsync("f1", Scenario.BoolFlag());                          // v1: f1
        await s.ApplyDevAsync("f2", Scenario.BoolFlag());                          // v2: f1, f2
        await s.ApplyDevAsync("f1", Scenario.BoolFlag(defaultVariant: "on"));      // v3: f1 changed

        var res = await s.As(s.Editor).PostAsJsonAsync(BaseUrl(s.Ns, "dev") + "/versions/1/rollback", new { reason = "oops" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(4, (await res.Json()).GetProperty("version").GetInt64());

        var v1 = (await (await s.As(s.Viewer).GetAsync(BaseUrl(s.Ns, "dev") + "/versions/1")).Json()).GetProperty("snapshot");
        var v4 = (await (await s.As(s.Viewer).GetAsync(BaseUrl(s.Ns, "dev") + "/versions/4")).Json()).GetProperty("snapshot");
        Assert.Equal(v1.GetProperty("flags").GetRawText(), v4.GetProperty("flags").GetRawText());
        Assert.Equal(4, v4.GetProperty("version").GetInt64());

        var versions = await (await s.As(s.Viewer).GetAsync(BaseUrl(s.Ns, "dev") + "/versions")).Json();
        Assert.Equal([4, 3, 2, 1], versions.EnumerateArray().Select(v => v.GetProperty("version").GetInt32()));
        Assert.Equal(HttpStatusCode.NotFound, (await s.As(s.Editor).PostAsJsonAsync(BaseUrl(s.Ns, "dev") + "/versions/99/rollback", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.As(s.Viewer).PostAsJsonAsync(BaseUrl(s.Ns, "dev") + "/versions/1/rollback", new { })).StatusCode);
    }

    [Fact]
    public async Task Promote_copies_flags_between_environments()
    {
        var s = await Scenario.CreateAsync(api);
        await s.CreateFlagAsync("f1");
        await s.CreateFlagAsync("f2");
        await s.ApplyDevAsync("f1", Scenario.BoolFlag(defaultVariant: "on"));
        await s.ApplyDevAsync("f2", Scenario.BoolFlag());

        var all = await s.As(s.Editor).PostAsJsonAsync(BaseUrl(s.Ns, "hml") + "/promote", new { fromEnvironment = "dev" });
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        var hml = await (await s.As(s.Viewer).GetAsync(BaseUrl(s.Ns, "hml") + "/config")).Json();
        Assert.Equal(1, hml.GetProperty("version").GetInt64());
        Assert.Equal("on", hml.GetProperty("flags").GetProperty("f1").GetProperty("defaultVariant").GetString());
        Assert.True(hml.GetProperty("flags").TryGetProperty("f2", out _));

        // subset promotion into prod goes through the CR policy
        var prod = await s.As(s.Editor).PostAsJsonAsync(BaseUrl(s.Ns, "prod") + "/promote", new { fromEnvironment = "hml", flagKeys = new[] { "f1" } });
        Assert.Equal(HttpStatusCode.Accepted, prod.StatusCode);
        var cr = (await prod.Json()).GetProperty("changeRequest");
        Assert.Equal(1, cr.GetProperty("operations").GetArrayLength());

        Assert.Equal(HttpStatusCode.BadRequest, (await s.As(s.Editor).PostAsJsonAsync(BaseUrl(s.Ns, "hml") + "/promote", new { fromEnvironment = "dev" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.As(s.Editor).PostAsJsonAsync(BaseUrl(s.Ns, "hml") + "/promote", new { fromEnvironment = "dev", flagKeys = new[] { "zzz" } })).StatusCode);
    }

    [Fact]
    public async Task Publishing_emits_pg_notify_with_namespace_environment_version()
    {
        var s = await Scenario.CreateAsync(api);
        await s.CreateFlagAsync("f1");

        await using var listener = new NpgsqlConnection(api.ConnectionString);
        await listener.OpenAsync();
        var received = new List<string>();
        listener.Notification += (_, e) => { if (e.Channel == "config_published") lock (received) received.Add(e.Payload); };
        await using (var cmd = new NpgsqlCommand("LISTEN config_published", listener)) await cmd.ExecuteNonQueryAsync();

        await s.ApplyDevAsync("f1", Scenario.BoolFlag());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            lock (received) if (received.Contains($"{s.Ns}/dev/1")) break;
            await listener.WaitAsync(cts.Token);
        }
    }

    [Fact]
    public async Task Service_access_grant_crud_notifies()
    {
        var s = await Scenario.CreateAsync(api);
        await using var listener = new NpgsqlConnection(api.ConnectionString);
        await listener.OpenAsync();
        var notifications = 0;
        listener.Notification += (_, e) => { if (e.Channel == "access_changed") Interlocked.Increment(ref notifications); };
        await using (var cmd = new NpgsqlCommand("LISTEN access_changed", listener)) await cmd.ExecuteNonQueryAsync();

        var url = $"/api/namespaces/{s.Ns}/service-access";
        var owner = s.Admin;
        var created = await owner.PostAsJsonAsync(url, new { clientId = "svc-" + s.Suffix, environment = "*" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Json()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(url, new { clientId = "svc-" + s.Suffix, environment = "*" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(url, new { clientId = "svc", environment = "nope" })).StatusCode);

        var list = await (await owner.GetAsync(url)).Json();
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal(1, (await api.QueryAsync("SELECT count(*) FROM service_access_grants WHERE namespace_key = @ns", r => r.GetInt64(0), ("ns", s.Ns)))[0]);

        Assert.Equal(HttpStatusCode.Forbidden, (await s.As(s.Editor).GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{url}/{id}")).StatusCode);
        Assert.Equal(0, (await (await owner.GetAsync(url)).Json()).GetArrayLength());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (Volatile.Read(ref notifications) < 2) await listener.WaitAsync(cts.Token);
    }

    [Fact]
    public async Task Members_policies_namespaces_and_environments_management()
    {
        var s = await Scenario.CreateAsync(api);
        var members = await (await s.Admin.GetAsync($"/api/namespaces/{s.Ns}/members")).Json();
        Assert.Equal(5, members.GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await s.As(s.Approver).GetAsync($"/api/namespaces/{s.Ns}/members")).StatusCode);

        var id = members.EnumerateArray().First(m => m.GetProperty("subject").GetString() == s.Viewer).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await s.Admin.DeleteAsync($"/api/namespaces/{s.Ns}/members/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.As(s.Viewer).GetAsync($"/api/namespaces/{s.Ns}")).StatusCode);

        var policies = await (await s.As(s.Approver).GetAsync($"/api/namespaces/{s.Ns}/policies")).Json();
        var prod = policies.EnumerateArray().Single(p => p.GetProperty("environment").GetString() == "prod");
        Assert.True(prod.GetProperty("requireChangeRequest").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await s.As(s.Editor).PutAsJsonAsync($"/api/namespaces/{s.Ns}/policies/dev", new { requireChangeRequest = true, requiredApprovals = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Admin.PutAsJsonAsync($"/api/namespaces/{s.Ns}/policies/dev", new { requireChangeRequest = true, requiredApprovals = 0 })).StatusCode);

        // admin-only
        Assert.Equal(HttpStatusCode.Forbidden, (await s.As(s.Approver).PostAsJsonAsync("/api/namespaces", new { key = "x" + s.Suffix, name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Admin.PostAsJsonAsync("/api/namespaces", new { key = s.Ns, name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.As(s.Editor).DeleteAsync($"/api/namespaces/{s.Ns}")).StatusCode);

        var envKey = "e" + s.Suffix;
        Assert.Equal(HttpStatusCode.Created, (await s.Admin.PostAsJsonAsync("/api/environments", new { key = envKey, name = "Extra", sortOrder = 9 })).StatusCode);
        Assert.Contains((await (await s.As(s.Editor).GetAsync("/api/environments")).Json()).EnumerateArray(), e => e.GetProperty("key").GetString() == envKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.As(s.Editor).PostAsJsonAsync("/api/environments", new { key = "zzz", name = "z" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await s.Admin.DeleteAsync($"/api/namespaces/{s.Ns}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Admin.GetAsync($"/api/namespaces/{s.Ns}")).StatusCode);
    }

    [Fact]
    public async Task Audit_log_records_changes_for_viewers()
    {
        var s = await Scenario.CreateAsync(api);
        await s.CreateFlagAsync("f1");
        await s.ApplyDevAsync("f1", Scenario.BoolFlag());
        var audit = await (await s.As(s.Viewer).GetAsync($"/api/audit?namespace={s.Ns}&take=50")).Json();
        var actions = audit.EnumerateArray().Select(a => a.GetProperty("action").GetString()).ToList();
        Assert.Contains("change.applied", actions);
        Assert.Contains("flag.created", actions);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor("nobody").GetAsync($"/api/audit?namespace={s.Ns}")).StatusCode);
    }

    [Fact]
    public async Task OpenApi_document_is_served()
    {
        var res = await api.ClientFor(null).GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
