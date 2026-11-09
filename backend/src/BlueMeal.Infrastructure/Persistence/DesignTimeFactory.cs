using BlueMeal.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BlueMeal.Infrastructure.Persistence;

/// <summary>Used by `dotnet ef` only. Connection string from BLUEMEAL_MIGRATION_DB or a local default.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        EncryptedStringConverter.Service ??= new AesGcmEncryptionService(
            "design:" + Convert.ToBase64String(new byte[32]), Convert.ToBase64String(new byte[32]));
        var cs = Environment.GetEnvironmentVariable("BLUEMEAL_MIGRATION_DB")
                 ?? "Host=localhost;Port=5442;Database=bluemeal;Username=bluemeal_owner;Password=dev_owner_pwd";
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(cs, n => n.MigrationsHistoryTable("__ef_migrations_history", AppDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AppDbContext(opts);
    }
}
