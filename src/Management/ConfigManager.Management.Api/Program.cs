using ConfigManager.Management.Api.Auth;
using ConfigManager.Management.Api.Common;
using ConfigManager.Management.Api.Data;
using ConfigManager.Management.Api.Features.Access;
using ConfigManager.Management.Api.Features.Audit;
using ConfigManager.Management.Api.Features.ChangeRequests;
using ConfigManager.Management.Api.Features.Changes;
using ConfigManager.Management.Api.Features.Environments;
using ConfigManager.Management.Api.Features.Flags;
using ConfigManager.Management.Api.Features.Me;
using ConfigManager.Management.Api.Features.Namespaces;
using ConfigManager.Management.Api.Features.Versions;
using ConfigManager.Management.Api.Services;
using ConfigManager.Management.Api.Startup;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

// Configuration is read lazily (inside option callbacks) so test hosts can override it.
services.AddDbContext<AppDbContext>((sp, o) =>
    o.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres"))
     .UseSnakeCaseNamingConvention());

services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((o, cfg) =>
    {
        var authority = cfg["Auth:Authority"];
        var metadata = cfg["Auth:MetadataAddress"];
        o.Authority = authority;
        if (!string.IsNullOrEmpty(metadata)) o.MetadataAddress = metadata;
        o.RequireHttpsMetadata = false;
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authority,
            ValidateAudience = true,
            ValidAudience = cfg["Auth:Audience"],
            NameClaimType = "preferred_username",
        };
    });
services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

services.AddCors();
services.AddOptions<CorsOptions>().Configure<IConfiguration>((o, cfg) =>
{
    var origins = cfg.GetSection("Cors:Origins").Get<string[]>() ?? [];
    o.AddDefaultPolicy(p =>
    {
        if (origins.Length > 0) p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
    });
});

services.AddHttpContextAccessor();
services.AddScoped<ICurrentUser>(sp => new CurrentUser(sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User ?? new()));
services.AddScoped<NamespaceAuthorizer>();
services.AddScoped<PublishingService>();
services.AddScoped<ChangeService>();
services.AddScoped<ChangeRequestService>();
services.AddScoped<SeedData>();
services.AddHostedService<DatabaseInitializer>();

services.AddProblemDetails();
services.AddExceptionHandler<ApiExceptionHandler>();
services.AddOpenApi();
services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("db", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" })).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") }).AllowAnonymous();
app.MapOpenApi().AllowAnonymous();

var api = app.MapGroup("/api");
api.MapMe();
api.MapEnvironments();
api.MapNamespaces();
api.MapFlags();
api.MapChanges();
api.MapVersions();
api.MapChangeRequests();
api.MapServiceAccess();
api.MapAudit();

app.Run();

public partial class Program;
