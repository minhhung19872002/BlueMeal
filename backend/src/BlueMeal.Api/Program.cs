using System.Globalization;
using System.IO.Compression;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BlueMeal.Api;
using BlueMeal.Api.Auth;
using BlueMeal.Api.Authorization;
using BlueMeal.Api.Errors;
using BlueMeal.Api.Infrastructure;
using BlueMeal.Application;
using BlueMeal.Application.Abstractions;
using BlueMeal.Application.Settings;
using BlueMeal.Infrastructure;
using BlueMeal.Infrastructure.Identity;
using BlueMeal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Formatting.Compact;

var isWorker = args.Contains("--worker") || string.Equals(Environment.GetEnvironmentVariable("BLUEMEAL_ROLE"), "worker", StringComparison.OrdinalIgnoreCase);

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables("BLUEMEAL__");
BlueMealConfig.ApplyEnvAliases(builder.Configuration);

// ---------------------------------------------------------------- logging (SPEC §10.3: JSON rolling file, syslog/SIEM via config)
builder.Host.UseSerilog((ctx, sp, lc) =>
{
    lc.ReadFrom.Configuration(ctx.Configuration)
      .Enrich.FromLogContext()
      .Enrich.WithProperty("app", "bluemeal")
      .Enrich.WithProperty("role", isWorker ? "worker" : "api");
    if (ctx.HostingEnvironment.IsDevelopment()) lc.WriteTo.Console();
    else lc.WriteTo.Console(new CompactJsonFormatter());
    var logPath = ctx.Configuration["Logging:FilePath"] ?? Path.Combine(AppContext.BaseDirectory, "logs", "bluemeal-.json");
    lc.WriteTo.File(new CompactJsonFormatter(), logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 90, fileSizeLimitBytes: 200_000_000, rollOnFileSizeLimit: true);
});

// ---------------------------------------------------------------- services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<HttpCurrentUser>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<HttpCurrentUser>());
builder.Services.AddInfrastructure(builder.Configuration, runScheduler: isWorker);
builder.Services.AddApplication();
builder.Services.AddBlueMealApi(builder.Configuration);

var jwt = builder.Configuration.GetSection("Auth").Get<JwtOptions>() ?? new JwtOptions();
jwt.Key = builder.Configuration["Auth:JwtKey"] ?? throw new InvalidOperationException("Auth:JwtKey (BLUEMEAL_JWT_KEY) missing");
if (jwt.Key.Length < 32) throw new InvalidOperationException("Auth:JwtKey must be ≥ 32 characters");
builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton<PasswordHashGate>();
// Data-protection keys shared by all API replicas and the worker through Redis (the containers have a read-only FS
// and no persistent profile); without Redis (tests) the default in-memory keys are kept.
builder.Services.AddDataProtection().SetApplicationName("BlueMeal");
builder.Services.AddOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>().Configure<IServiceProvider>((o, sp) =>
{
    if (sp.GetService<StackExchange.Redis.IConnectionMultiplexer>() is { } mux)
        o.XmlRepository = new Microsoft.AspNetCore.DataProtection.StackExchangeRedis.RedisXmlRepository(() => mux.GetDatabase(), "bluemeal:dp-keys");
});
builder.Services.AddScoped<CaptchaService>();
builder.Services.AddScoped<PermissionService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = true;
    o.TokenValidationParameters = new TokenService(jwt, TimeProvider.System).ValidationParameters();
    o.Events = new JwtBearerEvents
    {
        OnChallenge = async c => { c.HandleResponse(); await ProblemWriter.WriteAsync(c.HttpContext, 401, "error.unauthorized"); },
    };
});
builder.Services.AddBlueMealAuthorization();

builder.Services.AddControllers(o =>
    {
        o.RespectBrowserAcceptHeader = true;
        o.ReturnHttpNotAcceptable = false;
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    })
    .AddXmlSerializerFormatters()
    .ConfigureApiBehaviorOptions(o =>
    {
        o.InvalidModelStateResponseFactory = ctx =>
        {
            var errors = ctx.ModelState.Where(kv => kv.Value?.Errors.Count > 0)
                .ToDictionary(kv => kv.Key, kv => kv.Value!.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "invalid" : e.ErrorMessage).ToArray());
            var pd = ProblemWriter.Create(ctx.HttpContext, 400, "error.validation", errors: errors);
            return new Microsoft.AspNetCore.Mvc.ObjectResult(pd) { StatusCode = 400, ContentTypes = { "application/problem+json" } };
        };
    });

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
    o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/problem+json", "application/xml"]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

// Rate limiting (SPEC §10.5): per user (or IP) for API, stricter per IP for auth endpoints.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.OnRejected = async (c, _) => await ProblemWriter.WriteAsync(c.HttpContext, 429, "error.rateLimited");
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        if (!ctx.Request.Path.StartsWithSegments("/api")) return RateLimitPartition.GetNoLimiter("static");
        var key = BlueMeal.Api.Infrastructure.RateLimitKeys.ForRequest(ctx);
        var limit = builder.Configuration.GetValue("RateLimit:PerMinute", 600);
        return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = limit, TokensPerPeriod = limit, ReplenishmentPeriod = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true,
        });
    });
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter("auth:" + ctx.Connection.RemoteIpAddress, _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = builder.Configuration.GetValue("RateLimit:AuthPerMinute", 20), Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
    }));
    o.AddPolicy("forgot", ctx => RateLimitPartition.GetFixedWindowLimiter("forgot:" + ctx.Connection.RemoteIpAddress, _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = 5, Window = TimeSpan.FromMinutes(15), QueueLimit = 0,
    }));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
    o.ForwardLimit = 2;
});
builder.WebHost.ConfigureKestrel(k =>
{
    k.Limits.MaxRequestBodySize = builder.Configuration.GetValue<long>("Limits:MaxRequestBodyBytes", 25 * 1024 * 1024);
    k.AddServerHeader = false;
});

var hc = builder.Services.AddHealthChecks();
hc.AddNpgSql(builder.Configuration.GetConnectionString("Default")!, name: "postgres", tags: ["ready"]);
if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString("Redis")))
    hc.AddRedis(builder.Configuration.GetConnectionString("Redis")!, name: "redis", tags: ["ready"]);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "BlueMeal API", Version = "v1", Description = "Phần mềm bữa ăn ca thông minh — BlueMeal REST API (JSON; XML qua Accept: application/xml)" });
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = [] });
    o.CustomSchemaIds(t => t.FullName!.Replace("BlueMeal.", "").Replace("+", "."));
    var xml = Path.Combine(AppContext.BaseDirectory, "BlueMeal.Api.xml");
    if (File.Exists(xml)) o.IncludeXmlComments(xml);
});

// ---------------------------------------------------------------- pipeline
var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration, isWorker);
if (args.FirstOrDefault(a => a.StartsWith("--seed-loadtest=", StringComparison.Ordinal)) is { } loadTest)
{
    await BlueMeal.Api.Infrastructure.LoadTestSeeder.RunAsync(app.Services, int.Parse(loadTest["--seed-loadtest=".Length..], CultureInfo.InvariantCulture),
        Environment.GetEnvironmentVariable("BLUEMEAL_LOADTEST_PASSWORD"), app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("LoadTest"));
    return;
}
if (args.Contains("--migrate-only") || args.Contains("--seed-only"))
{
    Log.Information("Database initialization finished ({Mode}); exiting.", args.Contains("--seed-only") ? "seed" : "migrate");
    return;
}

var supported = new[] { new CultureInfo("vi-VN"), new CultureInfo("vi"), new CultureInfo("en-US"), new CultureInfo("en") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("vi-VN"),
    SupportedCultures = supported,
    SupportedUICultures = supported,
    RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider(), new QueryStringRequestCultureProvider()],
    FallBackToParentCultures = true, FallBackToParentUICultures = true,
});
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseResponseCompression();
app.UseSerilogRequestLogging(o => o.GetLevel = (ctx, _, ex) => ex != null || ctx.Response.StatusCode >= 500 ? Serilog.Events.LogEventLevel.Error
    : ctx.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose : Serilog.Events.LogEventLevel.Information);

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

if (isWorker)
{
    Log.Information("BlueMeal worker started (Quartz scheduler)");
    await app.RunAsync();
    return;
}

app.UseSwaggerGate();
app.UseSwagger();
app.UseSwaggerUI(o => { o.SwaggerEndpoint("/swagger/v1/swagger.json", "BlueMeal API v1"); o.DocumentTitle = "BlueMeal API"; });

app.UseMiddleware<ExpectedExceptionMiddleware>();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<SessionValidationMiddleware>();
app.UseRateLimiter();
app.UseMiddleware<MaintenanceMiddleware>();
app.UseMiddleware<PermissionEnforcementMiddleware>();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
