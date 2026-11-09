using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Settings;

namespace BlueMeal.Api;

public static class BlueMealConfig
{
    /// <summary>
    /// Maps flat environment variables / Docker secrets to configuration keys, so .env stays simple:
    /// BLUEMEAL_DB, BLUEMEAL_REDIS, BLUEMEAL_JWT_KEY, BLUEMEAL_ENC_KEYS, BLUEMEAL_HMAC_KEY, BLUEMEAL_PUBLIC_URL.
    /// A variable ending in _FILE is read from that file (Docker secret).
    /// </summary>
    public static void ApplyEnvAliases(ConfigurationManager config)
    {
        var map = new Dictionary<string, string>
        {
            ["BLUEMEAL_DB"] = "ConnectionStrings:Default",
            ["BLUEMEAL_REDIS"] = "ConnectionStrings:Redis",
            ["BLUEMEAL_JWT_KEY"] = "Auth:JwtKey",
            ["BLUEMEAL_ENC_KEYS"] = "Security:EncryptionKeys",
            ["BLUEMEAL_HMAC_KEY"] = "Security:HmacKey",
            ["BLUEMEAL_PUBLIC_URL"] = "App:PublicUrl",
            ["BLUEMEAL_ADMIN_PASSWORD"] = "Seed:AdminPassword",
            ["BLUEMEAL_FILES_PATH"] = "Storage:FilesPath",
            ["BLUEMEAL_BACKUP_PATH"] = "Storage:BackupPath",
            ["BLUEMEAL_EXPORT_PATH"] = "Storage:ExportPath",
        };
        var overrides = new Dictionary<string, string?>();
        foreach (var (env, key) in map)
        {
            var v = Environment.GetEnvironmentVariable(env);
            var file = Environment.GetEnvironmentVariable(env + "_FILE");
            if (!string.IsNullOrEmpty(file) && File.Exists(file)) v = File.ReadAllText(file).Trim();
            if (!string.IsNullOrEmpty(v)) overrides[key] = v;
        }
        if (overrides.Count > 0) config.AddInMemoryCollection(overrides);
    }
}

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddBlueMealApi(this IServiceCollection services, IConfiguration config)
    {
        return services;
    }

    /// <summary>Swagger only in Development/Staging, or when an admin enables it (setting system.swagger) — SPEC §2.</summary>
    public static IApplicationBuilder UseSwaggerGate(this WebApplication app)
    {
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/swagger") && !app.Environment.IsDevelopment() && !app.Environment.IsStaging())
            {
                var settings = ctx.RequestServices.GetRequiredService<ISettingsService>();
                var s = await settings.GetAsync<SwaggerSettings>(SettingKeys.Swagger, ctx.RequestAborted);
                if (!s.EnabledInProduction) { ctx.Response.StatusCode = 404; return; }
            }
            await next(ctx);
        });
        return app;
    }
}
