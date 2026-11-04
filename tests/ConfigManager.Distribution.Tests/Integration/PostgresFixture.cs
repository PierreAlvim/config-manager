using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;

namespace ConfigManager.Distribution.Tests.Integration;

/// <summary>One Postgres container shared by all integration tests; each test works in its own database.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Exact DDL from docs/ARCHITECTURE.md.
    public const string Ddl = """
        CREATE TABLE published_snapshots (
          id                 uuid PRIMARY KEY,
          namespace_key      text        NOT NULL,
          environment_key    text        NOT NULL,
          version            bigint      NOT NULL,
          content            jsonb       NOT NULL,
          published_at       timestamptz NOT NULL,
          published_by       text        NOT NULL,
          change_request_id  uuid        NULL,
          message            text        NULL,
          UNIQUE (namespace_key, environment_key, version)
        );

        CREATE TABLE service_access_grants (
          id               uuid PRIMARY KEY,
          client_id        text        NOT NULL,
          namespace_key    text        NOT NULL,
          environment_key  text        NOT NULL,
          created_at       timestamptz NOT NULL,
          created_by       text        NOT NULL,
          UNIQUE (client_id, namespace_key, environment_key)
        );
        """;

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Creates a fresh database (optionally with the contract tables) and returns its connection string.</summary>
    public async Task<TestDatabase> CreateDatabaseAsync(bool withSchema = true)
    {
        var name = "t_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {name}", admin);
            await cmd.ExecuteNonQueryAsync();
        }
        var cs = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name, Pooling = false }.ConnectionString;
        var db = new TestDatabase(cs);
        if (withSchema) await db.CreateSchemaAsync();
        return db;
    }
}

public sealed class TestDatabase(string connectionString)
{
    public string ConnectionString { get; } = connectionString;

    public Task CreateSchemaAsync() => ExecuteAsync(PostgresFixture.Ddl);

    public async Task ExecuteAsync(string sql, params object[] parameters)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters) cmd.Parameters.AddWithValue(p);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Inserts a snapshot row; when <paramref name="notify"/> is set also sends pg_notify like the Management API.</summary>
    public async Task PublishAsync(ConfigSnapshot snapshot, bool notify = true)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO published_snapshots (id, namespace_key, environment_key, version, content, published_at, published_by)
            VALUES ($1, $2, $3, $4, $5, now(), 'tester')
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue(Guid.NewGuid());
            cmd.Parameters.AddWithValue(snapshot.Namespace);
            cmd.Parameters.AddWithValue(snapshot.Environment);
            cmd.Parameters.AddWithValue(snapshot.Version);
            cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = SnapshotJson.Serialize(snapshot) });
            await cmd.ExecuteNonQueryAsync();
        }
        if (notify)
        {
            await using var n = new NpgsqlCommand("SELECT pg_notify('config_published', $1)", conn, tx);
            n.Parameters.AddWithValue($"{snapshot.Namespace}/{snapshot.Environment}/{snapshot.Version}");
            await n.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    public async Task GrantAsync(string clientId, string ns, string env, bool notify = true)
    {
        await ExecuteAsync(
            """
            INSERT INTO service_access_grants (id, client_id, namespace_key, environment_key, created_at, created_by)
            VALUES ($1, $2, $3, $4, now(), 'tester')
            """, Guid.NewGuid(), clientId, ns, env);
        if (notify) await ExecuteAsync("SELECT pg_notify('access_changed', '')");
    }

    public async Task RevokeAsync(string clientId)
    {
        await ExecuteAsync("DELETE FROM service_access_grants WHERE client_id = $1", clientId);
        await ExecuteAsync("SELECT pg_notify('access_changed', '')");
    }
}
