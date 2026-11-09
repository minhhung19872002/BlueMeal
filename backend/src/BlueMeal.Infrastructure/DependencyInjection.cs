using BlueMeal.Application.Abstractions;
using BlueMeal.Domain.Entities;
using BlueMeal.Infrastructure.Jobs;
using BlueMeal.Infrastructure.Persistence;
using BlueMeal.Infrastructure.Security;
using BlueMeal.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using StackExchange.Redis;

namespace BlueMeal.Infrastructure;

public static class DependencyInjection
{
    /// <param name="runScheduler">True for the worker process: start the Quartz scheduler and register recurring jobs.</param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, bool runScheduler = false)
    {
        var cs = config.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default missing");

        // Encryption keys from Docker secret / env (A-28)
        var enc = new AesGcmEncryptionService(
            config["Security:EncryptionKeys"] ?? throw new InvalidOperationException("Security:EncryptionKeys (BLUEMEAL_ENC_KEYS) missing"),
            config["Security:HmacKey"] ?? throw new InvalidOperationException("Security:HmacKey (BLUEMEAL_HMAC_KEY) missing"));
        EncryptedStringConverter.Service = enc;
        services.AddSingleton<IEncryptionService>(enc);
        services.AddSingleton(enc);
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<AppDbContext>((sp, o) =>
        {
            o.UseNpgsql(cs, npg =>
            {
                npg.MigrationsHistoryTable("__ef_migrations_history", AppDbContext.Schema);
                npg.EnableRetryOnFailure(3);
                npg.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });
            o.UseSnakeCaseNamingConvention();
            o.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddIdentityCore<AppUser>(o =>
            {
                // Policy is enforced by PasswordPolicyValidator from app_setting; Identity defaults relaxed here.
                o.Password.RequiredLength = 8;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.User.RequireUniqueEmail = false;
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        // PBKDF2-HMAC-SHA512 with 210k iterations (SPEC §10.1 requires ≥ 100k)
        services.Configure<PasswordHasherOptions>(o => { o.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3; o.IterationCount = 210_000; });

        // Redis (cache, distributed rate limit, revoked tokens). Fallback to in-memory when not configured.
        var redisCs = config.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisCs))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisCs + ",abortConnect=false"));
            services.AddSingleton<ICacheService, RedisCacheService>();
        }
        else services.AddSingleton<ICacheService, MemoryCacheService>();

        services.AddSingleton<IFileStorage>(_ => new LocalFileStorage(config["Storage:FilesPath"] ?? Path.Combine(AppContext.BaseDirectory, "data", "files")));
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<SampleDataSeeder>();
        services.AddScoped<IDocNumberGenerator, DocNumberGenerator>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<BlueMeal.Application.Catalog.IExcelService, BlueMeal.Infrastructure.Excel.ExcelService>();

        // Background jobs (A-34)
        var jobsMode = config["Jobs:Mode"] ?? "Quartz";
        if (jobsMode.Equals("Inline", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IBackgroundJobs, InlineBackgroundJobs>();
        }
        else
        {
            services.AddQuartz(q =>
            {
                q.SchedulerName = "BlueMeal";
                q.SchedulerId = "AUTO";
                q.UsePersistentStore(s =>
                {
                    s.UseProperties = true;
                    s.PerformSchemaValidation = false;
                    s.UsePostgres(p => { p.ConnectionString = cs; p.TablePrefix = "quartz.qrtz_"; });
                    s.UseSystemTextJsonSerializer();
                    s.UseClustering(c => { c.CheckinInterval = TimeSpan.FromSeconds(20); c.CheckinMisfireThreshold = TimeSpan.FromSeconds(60); });
                });
                q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 4);
                if (runScheduler)
                {
                    foreach (var rj in RecurringJobs(config))
                    {
                        var key = new JobKey(rj.Name, "recurring");
                        q.AddJob<AppJobRunner>(key, j => j.StoreDurably().UsingJobData(AppJobRunner.NameKey, rj.Name));
                        q.AddTrigger(t => t.ForJob(key).WithIdentity(rj.Name + "-trigger", "recurring")
                            .WithCronSchedule(ToQuartzCron(rj.Cron), c => c.InTimeZone(VietnamTimeZone()).WithMisfireHandlingInstructionFireAndProceed()));
                    }
                }
            });
            if (runScheduler) services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);
            services.AddSingleton<IBackgroundJobs, QuartzBackgroundJobs>();
        }

        return services;
    }

    /// <summary>Recurring jobs. Crons can be overridden via configuration Jobs:Cron:{name}.</summary>
    public static IEnumerable<RecurringJob> RecurringJobs(IConfiguration config)
    {
        string C(string name, string def) => config[$"Jobs:Cron:{name}"] ?? def;
        yield return new RecurringJob(JobNames.VddSync, C(JobNames.VddSync, "0 2 * * *"));
        yield return new RecurringJob(JobNames.Backup, C(JobNames.Backup, "0 1 * * *"));
        yield return new RecurringJob(JobNames.DailyAlerts, C(JobNames.DailyAlerts, "0 6 * * *"));
        yield return new RecurringJob(JobNames.Housekeeping, C(JobNames.Housekeeping, "30 3 * * *"));
    }

    /// <summary>Converts a 5-field unix cron (m h dom mon dow) to Quartz 6/7-field format.</summary>
    public static string ToQuartzCron(string cron)
    {
        var p = cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length != 5) return cron;
        var dom = p[2]; var dow = p[4];
        if (dow == "*") dow = "?"; else if (dom == "*") dom = "?";
        if (dom != "?" && dow != "?") dow = "?";
        if (dow != "?") dow = string.Join(',', dow.Split(',').Select(d => int.TryParse(d, out var n) ? ((n % 7) + 1).ToString() : d));
        return $"0 {p[0]} {p[1]} {dom} {p[3]} {dow}";
    }

    public static TimeZoneInfo VietnamTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
    }
}
