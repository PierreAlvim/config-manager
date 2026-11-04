using ConfigManager.Distribution.Api;
using ConfigManager.Distribution.Api.Background;
using ConfigManager.Distribution.Api.Caching;
using ConfigManager.Distribution.Api.Data;
using ConfigManager.Distribution.Api.Endpoints;
using ConfigManager.Distribution.Api.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

services.Configure<DistributionOptions>(builder.Configuration.GetSection(DistributionOptions.SectionName));
services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));

services.AddSingleton(sp => new DatabaseOptions(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.")));
services.AddSingleton(sp => NpgsqlDataSource.Create(sp.GetRequiredService<DatabaseOptions>().ConnectionString));
services.AddSingleton<ISnapshotSource, PostgresSnapshotSource>();
services.AddSingleton<SnapshotBroadcaster>();
services.AddSingleton<SnapshotStore>();
services.AddSingleton<AccessGrantStore>();
services.AddSingleton<NotificationDispatcher>();
services.AddHostedService<PostgresListener>();

services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IConfiguration>((o, config) =>
    {
        var auth = config.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        o.Authority = auth.Authority;
        if (!string.IsNullOrEmpty(auth.MetadataAddress)) o.MetadataAddress = auth.MetadataAddress;
        o.RequireHttpsMetadata = false;
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = auth.Authority,
            ValidAudience = auth.Audience,
            NameClaimType = "preferred_username",
        };
    });
services.AddAuthorization();

services.AddProblemDetails();
services.AddExceptionHandler<DatabaseUnavailableHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapSnapshotEndpoints();
app.MapOfrepEndpoints();

app.Run();

public partial class Program;
