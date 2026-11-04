using System.Threading.Channels;
using ConfigManager.Distribution.Api.Caching;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ConfigManager.Distribution.Api.Background;

/// <summary>
/// Holds a dedicated connection doing LISTEN on <c>config_published</c> / <c>access_changed</c> (reconnecting with
/// backoff) and runs a safety poll every <see cref="DistributionOptions.PollInterval"/>.
/// </summary>
public sealed class PostgresListener(
    DatabaseOptions database,
    NotificationDispatcher dispatcher,
    SnapshotStore snapshots,
    AccessGrantStore grants,
    IOptions<DistributionOptions> options,
    ILogger<PostgresListener> logger) : BackgroundService
{
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(ListenLoopAsync(stoppingToken), PollLoopAsync(stoppingToken));

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ListenAsync(() => backoff = TimeSpan.FromSeconds(1), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Notification listener failed; reconnecting in {Delay}", backoff);
            }

            await DelayAsync(backoff, ct);
            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, MaxBackoff.TotalSeconds));
        }
    }

    private async Task ListenAsync(Action onConnected, CancellationToken ct)
    {
        var csb = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Pooling = false, KeepAlive = 30 };
        await using var conn = new NpgsqlConnection(csb.ConnectionString);
        var pending = Channel.CreateUnbounded<(string Channel, string Payload)>();
        conn.Notification += (_, e) => pending.Writer.TryWrite((e.Channel, e.Payload));

        await conn.OpenAsync(ct);
        await using (var cmd = new NpgsqlCommand(
            $"LISTEN {NotificationDispatcher.ConfigPublishedChannel}; LISTEN {NotificationDispatcher.AccessChangedChannel};", conn))
            await cmd.ExecuteNonQueryAsync(ct);
        logger.LogInformation("Listening for Postgres notifications");
        onConnected();

        // Notifications may have been missed while disconnected.
        await SyncSafelyAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            await conn.WaitAsync(ct);
            while (pending.Reader.TryRead(out var n))
            {
                try
                {
                    await dispatcher.DispatchAsync(n.Channel, n.Payload, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Failed to handle notification on {Channel}", n.Channel);
                }
            }
        }
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(options.Value.PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await SyncSafelyAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    /// <summary>Reloads grants and refreshes stale snapshots; tolerates missing tables and other transient errors.</summary>
    private async Task SyncSafelyAsync(CancellationToken ct)
    {
        try
        {
            await grants.ReloadAsync(ct);
            await snapshots.SyncAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            logger.LogInformation("Tables not created yet ({Message}); will retry", ex.MessageText);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Sync with database failed");
        }
    }

    private static async Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
