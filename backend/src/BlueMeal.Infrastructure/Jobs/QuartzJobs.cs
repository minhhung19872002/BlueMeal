using BlueMeal.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Quartz;

namespace BlueMeal.Infrastructure.Jobs;

/// <summary>Quartz job that resolves an <see cref="IAppJob"/> by name and runs it inside a fresh DI scope.</summary>
[DisallowConcurrentExecution]
public sealed class AppJobRunner(IServiceScopeFactory scopes, ILogger<AppJobRunner> log) : IJob
{
    public const string NameKey = "bm.job";

    public async Task Execute(IJobExecutionContext context)
    {
        var map = context.MergedJobDataMap;
        var name = map.GetString(NameKey) ?? throw new InvalidOperationException("Job name missing");
        var data = map.Keys.Where(k => k != NameKey).ToDictionary(k => k, k => map.GetString(k) ?? "");
        await using var scope = scopes.CreateAsyncScope();
        var job = scope.ServiceProvider.GetServices<IAppJob>().FirstOrDefault(j => j.Name == name)
                  ?? throw new InvalidOperationException($"Unknown job '{name}'");
        log.LogInformation("Running job {Job}", name);
        try { await job.ExecuteAsync(data, context.CancellationToken); }
        catch (Exception ex) { log.LogError(ex, "Job {Job} failed", name); throw new JobExecutionException(ex, refireImmediately: false); }
    }
}

/// <summary>Queues one-off jobs into the persistent Quartz store; the worker service executes them (A-34).</summary>
public sealed class QuartzBackgroundJobs(ISchedulerFactory factory) : IBackgroundJobs
{
    public async Task EnqueueAsync(string jobName, IDictionary<string, string>? data = null, CancellationToken ct = default)
    {
        var scheduler = await factory.GetScheduler(ct);
        var map = new JobDataMap { [AppJobRunner.NameKey] = jobName };
        if (data != null) foreach (var kv in data) map[kv.Key] = kv.Value;
        var id = Guid.NewGuid().ToString("N");
        var job = JobBuilder.Create<AppJobRunner>().WithIdentity($"{jobName}-{id}", "adhoc").UsingJobData(map).RequestRecovery().Build();
        var trigger = TriggerBuilder.Create().WithIdentity($"t-{id}", "adhoc").StartNow().Build();
        await scheduler.ScheduleJob(job, trigger, ct);
    }
}

/// <summary>Runs jobs immediately in-process (tests, single-node dev without worker).</summary>
public sealed class InlineBackgroundJobs(IServiceScopeFactory scopes, ILogger<InlineBackgroundJobs> log) : IBackgroundJobs
{
    public Task EnqueueAsync(string jobName, IDictionary<string, string>? data = null, CancellationToken ct = default)
    {
        var copy = new Dictionary<string, string>(data ?? new Dictionary<string, string>());
        _ = Task.Run(async () =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var job = scope.ServiceProvider.GetServices<IAppJob>().FirstOrDefault(j => j.Name == jobName);
            if (job is null) { log.LogError("Unknown job {Job}", jobName); return; }
            try { await job.ExecuteAsync(copy, CancellationToken.None); }
            catch (Exception ex) { log.LogError(ex, "Inline job {Job} failed", jobName); }
        }, CancellationToken.None);
        return Task.CompletedTask;
    }
}

/// <summary>Recurring job definitions (cron in Asia/Ho_Chi_Minh).</summary>
public sealed record RecurringJob(string Name, string Cron);

public static class QuartzSchema
{
    /// <summary>Creates Quartz ADO job-store tables (schema "quartz") if missing. Idempotent.</summary>
    public static async Task EnsureAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = Sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private const string Sql = """
        CREATE SCHEMA IF NOT EXISTS quartz;
        CREATE TABLE IF NOT EXISTS quartz.qrtz_job_details (
          sched_name TEXT NOT NULL, job_name TEXT NOT NULL, job_group TEXT NOT NULL, description TEXT NULL,
          job_class_name TEXT NOT NULL, is_durable BOOL NOT NULL, is_nonconcurrent BOOL NOT NULL, is_update_data BOOL NOT NULL,
          requests_recovery BOOL NOT NULL, job_data BYTEA NULL, PRIMARY KEY (sched_name, job_name, job_group));
        CREATE TABLE IF NOT EXISTS quartz.qrtz_triggers (
          sched_name TEXT NOT NULL, trigger_name TEXT NOT NULL, trigger_group TEXT NOT NULL, job_name TEXT NOT NULL, job_group TEXT NOT NULL,
          description TEXT NULL, next_fire_time BIGINT NULL, prev_fire_time BIGINT NULL, priority INTEGER NULL, trigger_state TEXT NOT NULL,
          trigger_type TEXT NOT NULL, start_time BIGINT NOT NULL, end_time BIGINT NULL, calendar_name TEXT NULL, misfire_instr SMALLINT NULL,
          job_data BYTEA NULL, PRIMARY KEY (sched_name, trigger_name, trigger_group),
          FOREIGN KEY (sched_name, job_name, job_group) REFERENCES quartz.qrtz_job_details(sched_name, job_name, job_group));
        CREATE TABLE IF NOT EXISTS quartz.qrtz_simple_triggers (
          sched_name TEXT NOT NULL, trigger_name TEXT NOT NULL, trigger_group TEXT NOT NULL, repeat_count BIGINT NOT NULL,
          repeat_interval BIGINT NOT NULL, times_triggered BIGINT NOT NULL, PRIMARY KEY (sched_name, trigger_name, trigger_group),
          FOREIGN KEY (sched_name, trigger_name, trigger_group) REFERENCES quartz.qrtz_triggers(sched_name, trigger_name, trigger_group) ON DELETE CASCADE);
        CREATE TABLE IF NOT EXISTS quartz.qrtz_simprop_triggers (
          sched_name TEXT NOT NULL, trigger_name TEXT NOT NULL, trigger_group TEXT NOT NULL, str_prop_1 TEXT NULL, str_prop_2 TEXT NULL,
          str_prop_3 TEXT NULL, int_prop_1 INTEGER NULL, int_prop_2 INTEGER NULL, long_prop_1 BIGINT NULL, long_prop_2 BIGINT NULL,
          dec_prop_1 NUMERIC NULL, dec_prop_2 NUMERIC NULL, bool_prop_1 BOOL NULL, bool_prop_2 BOOL NULL, time_zone_id TEXT NULL,
          PRIMARY KEY (sched_name, trigger_name, trigger_group),
          FOREIGN KEY (sched_name, trigger_name, trigger_group) REFERENCES quartz.qrtz_triggers(sched_name, trigger_name, trigger_group) ON DELETE CASCADE);
        CREATE TABLE IF NOT EXISTS quartz.qrtz_cron_triggers (
          sched_name TEXT NOT NULL, trigger_name TEXT NOT NULL, trigger_group TEXT NOT NULL, cron_expression TEXT NOT NULL, time_zone_id TEXT,
          PRIMARY KEY (sched_name, trigger_name, trigger_group),
          FOREIGN KEY (sched_name, trigger_name, trigger_group) REFERENCES quartz.qrtz_triggers(sched_name, trigger_name, trigger_group) ON DELETE CASCADE);
        CREATE TABLE IF NOT EXISTS quartz.qrtz_blob_triggers (
          sched_name TEXT NOT NULL, trigger_name TEXT NOT NULL, trigger_group TEXT NOT NULL, blob_data BYTEA NULL,
          PRIMARY KEY (sched_name, trigger_name, trigger_group),
          FOREIGN KEY (sched_name, trigger_name, trigger_group) REFERENCES quartz.qrtz_triggers(sched_name, trigger_name, trigger_group) ON DELETE CASCADE);
        CREATE TABLE IF NOT EXISTS quartz.qrtz_calendars (sched_name TEXT NOT NULL, calendar_name TEXT NOT NULL, calendar BYTEA NOT NULL, PRIMARY KEY (sched_name, calendar_name));
        CREATE TABLE IF NOT EXISTS quartz.qrtz_paused_trigger_grps (sched_name TEXT NOT NULL, trigger_group TEXT NOT NULL, PRIMARY KEY (sched_name, trigger_group));
        CREATE TABLE IF NOT EXISTS quartz.qrtz_fired_triggers (
          sched_name TEXT NOT NULL, entry_id TEXT NOT NULL, trigger_name TEXT NOT NULL, trigger_group TEXT NOT NULL, instance_name TEXT NOT NULL,
          fired_time BIGINT NOT NULL, sched_time BIGINT NOT NULL, priority INTEGER NOT NULL, state TEXT NOT NULL, job_name TEXT NULL,
          job_group TEXT NULL, is_nonconcurrent BOOL NOT NULL, requests_recovery BOOL NULL, PRIMARY KEY (sched_name, entry_id));
        CREATE TABLE IF NOT EXISTS quartz.qrtz_scheduler_state (
          sched_name TEXT NOT NULL, instance_name TEXT NOT NULL, last_checkin_time BIGINT NOT NULL, checkin_interval BIGINT NOT NULL,
          PRIMARY KEY (sched_name, instance_name));
        CREATE TABLE IF NOT EXISTS quartz.qrtz_locks (sched_name TEXT NOT NULL, lock_name TEXT NOT NULL, PRIMARY KEY (sched_name, lock_name));
        CREATE INDEX IF NOT EXISTS idx_qrtz_t_next_fire_time ON quartz.qrtz_triggers(next_fire_time);
        CREATE INDEX IF NOT EXISTS idx_qrtz_t_state ON quartz.qrtz_triggers(trigger_state);
        CREATE INDEX IF NOT EXISTS idx_qrtz_ft_trig_name ON quartz.qrtz_fired_triggers(trigger_name);
        -- optional 3.x columns (additive schema changes): misfire original fire time, node affinity
        ALTER TABLE quartz.qrtz_triggers ADD COLUMN IF NOT EXISTS misfire_orig_fire_time BIGINT NULL;
        ALTER TABLE quartz.qrtz_triggers ADD COLUMN IF NOT EXISTS preferred_node TEXT NULL;
        ALTER TABLE quartz.qrtz_triggers ADD COLUMN IF NOT EXISTS preferred_node_auto BOOL NOT NULL DEFAULT false;
        """;
}
