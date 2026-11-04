using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ConfigManager.Distribution.Tests.Integration;

/// <summary>Test authentication: the client id comes from <c>X-Test-Client</c> (becomes claim <c>azp</c>); no header = 401.</summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string Header = "X-Test-Client";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var client) || string.IsNullOrEmpty(client))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity([new Claim("azp", client.ToString())], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

public sealed class DistributionFactory(
    string connectionString, TimeSpan? pollInterval = null, TimeSpan? keepAlive = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", connectionString);
        builder.UseSetting("Distribution:PollInterval", (pollInterval ?? TimeSpan.FromSeconds(30)).ToString());
        builder.UseSetting("Distribution:KeepAliveInterval", (keepAlive ?? TimeSpan.FromSeconds(15)).ToString());
        builder.UseSetting("Auth:Authority", "http://localhost:0/realms/test");
        builder.ConfigureTestServices(services =>
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { }));
    }

    public HttpClient CreateClientFor(string? clientId)
    {
        var client = CreateClient();
        if (clientId is not null) client.DefaultRequestHeaders.Add(TestAuthHandler.Header, clientId);
        return client;
    }
}
