using ConfigManager.Management.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ConfigManager.Management.Api.Startup;

/// <summary>Applies migrations (retrying until the database is reachable) and optionally seeds demo data.</summary>
public sealed class DatabaseInitializer(IServiceProvider services, IConfiguration config, ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        const int maxAttempts = 30;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.MigrateAsync(ct);
                if (config.GetValue<bool>("Seed:Enabled"))
                    await scope.ServiceProvider.GetRequiredService<SeedData>().SeedIfEmptyAsync(ct);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Database not ready (attempt {Attempt}/{Max}); retrying in 2s", attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
