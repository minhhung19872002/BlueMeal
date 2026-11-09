using BlueMeal.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BlueMeal.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>
    /// Applies migrations (as owner user) and seeds reference data under a PostgreSQL advisory lock so that
    /// several replicas starting together do not race. Controlled by Database:AutoMigrate (default true).
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider root, IConfiguration config, bool isWorker, CancellationToken ct = default)
    {
        await using var scope = root.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
        var autoMigrate = config.GetValue("Database:AutoMigrate", true);
        var seed = config.GetValue("Database:Seed", true);
        var migrationCs = config.GetConnectionString("Migration") ?? config.GetConnectionString("Default")!;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var lockConn = new NpgsqlConnection(migrationCs);
                await lockConn.OpenAsync(ct);
                await using (var l = new NpgsqlCommand("SELECT pg_advisory_lock(424242)", lockConn)) await l.ExecuteNonQueryAsync(ct);
                try
                {
                    if (autoMigrate)
                    {
                        var opts = new DbContextOptionsBuilder<AppDbContext>()
                            .UseNpgsql(migrationCs, n => n.MigrationsHistoryTable("__ef_migrations_history", AppDbContext.Schema))
                            .UseSnakeCaseNamingConvention().Options;
                        await using var mdb = new AppDbContext(opts);
                        var pending = (await mdb.Database.GetPendingMigrationsAsync(ct)).ToList();
                        if (pending.Count > 0)
                        {
                            log.LogInformation("Applying {Count} migration(s): {List}", pending.Count, string.Join(", ", pending));
                            await mdb.Database.MigrateAsync(ct);
                        }
                        if (!string.Equals(config["Jobs:Mode"], "Inline", StringComparison.OrdinalIgnoreCase))
                            await QuartzSchema.EnsureAsync(migrationCs, ct);
                        await using (var p = new NpgsqlCommand("SELECT app.ensure_audit_partitions(3)", lockConn)) await p.ExecuteNonQueryAsync(ct);
                    }
                    if (seed && !isWorker)
                        await ActivatorUtilities.CreateInstance<Seeder>(sp).SeedAsync(ct);
                }
                finally
                {
                    await using var u = new NpgsqlCommand("SELECT pg_advisory_unlock(424242)", lockConn);
                    await u.ExecuteNonQueryAsync(ct);
                }
                return;
            }
            catch (NpgsqlException ex) when (attempt < 10)
            {
                log.LogWarning(ex, "Database not ready (attempt {Attempt}), retrying…", attempt);
                await Task.Delay(TimeSpan.FromSeconds(3 * attempt), ct);
            }
        }
    }
}
