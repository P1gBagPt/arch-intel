using System.Data;
using System.Reflection;
using ArchIntel.GraphStore.Core;
using Dapper;

namespace ArchIntel.GraphStore.Sqlite;

/// <summary>
/// Minimal migration runner: applies embedded .sql scripts under Migrations/ in filename order,
/// tracking what's already been applied in a schema_migrations table. Deliberately hand-rolled
/// instead of pulling in DbUp/FluentMigrator (Phase 1 schema is small and scripts are plain SQL).
/// </summary>
public sealed class MigrationRunner
{
    private readonly IDbConnectionFactory _connectionFactory;

    public MigrationRunner(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task ApplyAsync(CancellationToken ct = default)
    {
        using var connection = await _connectionFactory.OpenConnectionAsync(ct);

        await connection.ExecuteAsync(
            "CREATE TABLE IF NOT EXISTS schema_migrations (name TEXT PRIMARY KEY, applied_at TEXT NOT NULL);");

        var applied = (await connection.QueryAsync<string>("SELECT name FROM schema_migrations"))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (name, sql) in ReadEmbeddedMigrations())
        {
            if (applied.Contains(name))
            {
                continue;
            }

            using var transaction = connection.BeginTransaction();
            await connection.ExecuteAsync(sql, transaction: transaction);
            await connection.ExecuteAsync(
                "INSERT INTO schema_migrations (name, applied_at) VALUES (@Name, @AppliedAt)",
                new { Name = name, AppliedAt = DateTimeOffset.UtcNow.ToString("O") },
                transaction);
            transaction.Commit();
        }
    }

    private static IEnumerable<(string Name, string Sql)> ReadEmbeddedMigrations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(n => n.Contains("Migrations.", StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal);

        foreach (var resourceName in resourceNames)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded migration resource '{resourceName}' not found.");
            using var reader = new StreamReader(stream);
            yield return (resourceName, reader.ReadToEnd());
        }
    }
}
