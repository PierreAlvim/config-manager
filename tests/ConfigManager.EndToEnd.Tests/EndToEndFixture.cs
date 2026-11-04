extern alias mgmt;
extern alias dist;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using ConfigManager.OpenFeature;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace ConfigManager.EndToEnd.Tests;

/// <summary>
/// Emits Keycloak-shaped claims from headers: users (X-Test-User/Groups/Admin) for the Management API,
/// service clients (X-Test-Client -> azp) for the Distribution API.
/// </summary>
public sealed class HeaderAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
    : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
{
    public const string Scheme = "E2E";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>();
        if (Request.Headers.TryGetValue("X-Test-Client", out var client) && !string.IsNullOrEmpty(client))
            claims.Add(new Claim("azp", client!));

        if (Request.Headers.TryGetValue("X-Test-User", out var user) && !string.IsNullOrEmpty(user))
        {
            claims.Add(new Claim("sub", user!));
            claims.Add(new Claim("preferred_username", user!));
            claims.Add(new Claim("name", user!));
            foreach (var g in Request.Headers["X-Test-Groups"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
                claims.Add(new Claim("groups", g));
            var admin = Request.Headers["X-Test-Admin"] == "true" ? ",\"cm-admin\"" : "";
            claims.Add(new Claim("realm_access", $"{{\"roles\":[\"default-roles-config-manager\"{admin}]}}"));
        }

        if (claims.Count == 0) return Task.FromResult(AuthenticateResult.NoResult());
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme, "preferred_username", "roles"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme)));
    }
}

internal static class TestAuth
{
    public static void Use(IWebHostBuilder builder) => builder.ConfigureTestServices(services =>
        services.AddAuthentication(o =>
        {
            o.DefaultScheme = HeaderAuthHandler.Scheme;
            o.DefaultAuthenticateScheme = HeaderAuthHandler.Scheme;
            o.DefaultChallengeScheme = HeaderAuthHandler.Scheme;
        }).AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>(HeaderAuthHandler.Scheme, _ => { }));
}

public sealed class ManagementFactory(string cs) : WebApplicationFactory<mgmt::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", cs);
        builder.UseSetting("Seed:Enabled", "true");
        builder.UseSetting("Auth:Authority", "http://localhost:0/realms/test");
        builder.UseSetting("Auth:Audience", "config-manager-api");
        TestAuth.Use(builder);
    }
}

public sealed class DistributionFactory(string cs) : WebApplicationFactory<dist::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", cs);
        builder.UseSetting("Distribution:PollInterval", "00:00:05");
        builder.UseSetting("Auth:Authority", "http://localhost:0/realms/test");
        builder.UseSetting("Auth:Audience", "config-distribution");
        TestAuth.Use(builder);
    }
}

/// <summary>
/// Plays the IdP for the SDK: answers the client-credentials request with a fake token and
/// forwards everything else to the in-memory Distribution API, tagging the caller's client id.
/// </summary>
internal sealed class FakeIdpHandler(string clientId, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public const string TokenEndpoint = "http://idp.test/token";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri!.ToString() == TokenEndpoint)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"fake","token_type":"Bearer","expires_in":300}""", Encoding.UTF8, "application/json"),
            });

        request.Headers.Add("X-Test-Client", clientId);
        return base.SendAsync(request, ct);
    }
}

/// <summary>One Postgres + Management API + Distribution API shared by all end-to-end tests.</summary>
public sealed class EndToEndFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
    public ManagementFactory Management { get; private set; } = null!;
    public DistributionFactory Distribution { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        Management = new ManagementFactory(_pg.GetConnectionString());
        _ = Management.Server; // migrate + seed before distribution starts listening
        Distribution = new DistributionFactory(_pg.GetConnectionString());
        _ = Distribution.Server;
    }

    public async Task DisposeAsync()
    {
        await Distribution.DisposeAsync();
        await Management.DisposeAsync();
        await _pg.DisposeAsync();
    }

    public HttpClient User(string username, string? groups = null, bool admin = false)
    {
        var c = Management.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-User", username);
        if (groups is not null) c.DefaultRequestHeaders.Add("X-Test-Groups", groups);
        if (admin) c.DefaultRequestHeaders.Add("X-Test-Admin", "true");
        return c;
    }

    public HttpClient Service(string clientId)
    {
        var c = Distribution.CreateClient();
        c.DefaultRequestHeaders.Add("X-Test-Client", clientId);
        return c;
    }

    /// <summary>Creates a real ConfigManager OpenFeature provider wired to the in-memory Distribution API.</summary>
    public async Task<ConfigManagerProvider> StartProviderAsync(string clientId, string ns, string env)
    {
        var http = new HttpClient(new FakeIdpHandler(clientId, Distribution.Server.CreateHandler()))
        {
            Timeout = Timeout.InfiniteTimeSpan, // SSE stream is long-lived
        };
        var provider = ConfigManagerProvider.Create(new ConfigManagerProviderOptions
        {
            DistributionUrl = Distribution.Server.BaseAddress.ToString().TrimEnd('/'),
            Namespace = ns,
            Environment = env,
            TokenEndpoint = FakeIdpHandler.TokenEndpoint,
            ClientId = clientId,
            ClientSecret = "unused",
        }, http);
        await provider.InitializeAsync(global::OpenFeature.Model.EvaluationContext.Empty);
        return provider;
    }
}

[CollectionDefinition(Name)]
public sealed class EndToEndCollection : ICollectionFixture<EndToEndFixture>
{
    public const string Name = "e2e";
}
