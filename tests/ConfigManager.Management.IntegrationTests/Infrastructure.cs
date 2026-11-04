using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using ConfigManager.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace ConfigManager.Management.IntegrationTests;

/// <summary>Builds the same claim shapes Keycloak emits, from X-Test-* request headers.</summary>
public sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User", out var user) || string.IsNullOrEmpty(user))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("preferred_username", user.ToString()),
            new("name", $"{user} Test"),
            new("email", $"{user}@example.com"),
        };
        foreach (var g in Request.Headers["X-Test-Groups"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            claims.Add(new Claim("groups", g));
        var roles = Request.Headers["X-Test-Admin"] == "true" ? "\"cm-admin\"" : "";
        claims.Add(new Claim("realm_access", $"{{\"roles\":[\"default-roles-config-manager\"{(roles.Length > 0 ? "," + roles : "")}]}}"));

        var identity = new ClaimsIdentity(claims, Scheme, "preferred_username", "roles");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}

public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _ = Server; // boots the host: migrations + seed
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = ConnectionString,
            ["Seed:Enabled"] = "true",
        }));
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(o =>
            {
                o.DefaultScheme = TestAuthHandler.Scheme;
                o.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                o.DefaultChallengeScheme = TestAuthHandler.Scheme;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
        });
    }

    public HttpClient ClientFor(string? user, string? groups = null, bool admin = false)
    {
        var c = CreateClient();
        if (user is not null) c.DefaultRequestHeaders.Add("X-Test-User", user);
        if (groups is not null) c.DefaultRequestHeaders.Add("X-Test-Groups", groups);
        if (admin) c.DefaultRequestHeaders.Add("X-Test-Admin", "true");
        return c;
    }

    public async Task<List<T>> QueryAsync<T>(string sql, Func<NpgsqlDataReader, T> map, params (string, object)[] args)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<T>();
        while (await r.ReadAsync()) list.Add(map(r));
        return list;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}

/// <summary>A fresh namespace with one user per role, so tests are independent of the seed and of each other.</summary>
public sealed class Scenario
{
    public required ApiFixture Api { get; init; }
    public required string Ns { get; init; }
    public string Suffix { get; init; } = "";
    public HttpClient Admin => Api.ClientFor("root", admin: true);
    public string Editor => "ed" + Suffix;
    public string Editor2 => "ed2" + Suffix;
    public string Approver => "ap" + Suffix;
    public string Approver2 => "ap2" + Suffix;
    public string Viewer => "vw" + Suffix;
    public HttpClient As(string user) => Api.ClientFor(user);

    public static async Task<Scenario> CreateAsync(ApiFixture api)
    {
        var suffix = Guid.NewGuid().ToString("n")[..8];
        var s = new Scenario { Api = api, Ns = "t" + suffix, Suffix = suffix };
        var admin = s.Admin;
        (await admin.PostAsJsonAsync("/api/namespaces", new { key = s.Ns, name = s.Ns, description = "test" })).EnsureSuccessStatusCode();
        foreach (var (user, role) in new[] { (s.Editor, "editor"), (s.Editor2, "editor"), (s.Approver, "approver"), (s.Approver2, "approver"), (s.Viewer, "viewer") })
            (await admin.PostAsJsonAsync($"/api/namespaces/{s.Ns}/members", new { subjectType = "user", subject = user, role })).EnsureSuccessStatusCode();
        return s;
    }

    public async Task CreateFlagAsync(string key, string type = "boolean")
    {
        var res = await As(Editor).PostAsJsonAsync($"/api/namespaces/{Ns}/flags", new { key, name = key, description = "", type });
        res.EnsureSuccessStatusCode();
    }

    public static object BoolFlag(bool enabled = true, string defaultVariant = "off") => new
    {
        type = "boolean", enabled,
        variants = new Dictionary<string, bool> { ["on"] = true, ["off"] = false },
        defaultVariant,
    };

    public static object Upsert(string key, object flag) => new { type = "upsert", flagKey = key, flag };

    public Task<HttpResponseMessage> ChangeAsync(string user, string env, string title, bool force = false, params object[] ops) =>
        As(user).PostAsJsonAsync($"/api/namespaces/{Ns}/environments/{env}/changes", new { title, forceChangeRequest = force, operations = ops });

    /// <summary>Applies one flag upsert directly in dev and returns the new version.</summary>
    public async Task<long> ApplyDevAsync(string key, object flag, string? user = null)
    {
        var res = await ChangeAsync(user ?? Editor, "dev", "apply " + key, false, Upsert(key, flag));
        Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("version").GetInt64();
    }

    public async Task<Guid> OpenProdCrAsync(string key, object flag, string? user = null)
    {
        var res = await ChangeAsync(user ?? Editor, "prod", "cr " + key, false, Upsert(key, flag));
        Assert.Equal(System.Net.HttpStatusCode.Accepted, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changeRequest").GetProperty("id").GetGuid();
    }
}

public static class Http
{
    public static async Task<JsonElement> Json(this HttpResponseMessage res) => await res.Content.ReadFromJsonAsync<JsonElement>();
}
