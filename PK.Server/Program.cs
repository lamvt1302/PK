using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PK.Server.Common;
using PK.Server.Data;
using PK.Server.Middleware;
using PK.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.EnableAnnotations();
});

// CORS — permissive dev policy. In production override with a stricter policy
// via configuration (AllowedOrigins) or environment-specific appsettings.
builder.Services.AddCors(options =>
{
    options.AddPolicy("dev-permissive", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials()
              .WithExposedHeaders("X-Request-Id");
    });
});

builder.Services.AddRateLimiter(options =>
{
    // Baseline: /spin 10 req/phút, /island/upgrade 20 req/phút, /attack/resolve 20 req/phút.
    // Partition theo player_id nếu đã auth, fallback theo remote IP.
    options.AddPolicy("spin", httpContext =>
    {
        var key = httpContext.Items.TryGetValue("player_id", out var pid) ? pid?.ToString() : null;
        key ??= httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 60,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            TokensPerPeriod = 60,
            AutoReplenishment = true
        });
    });

    options.AddPolicy("upgrade", httpContext =>
    {
        var key = httpContext.Items.TryGetValue("player_id", out var pid) ? pid?.ToString() : null;
        key ??= httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 20,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            TokensPerPeriod = 20,
            AutoReplenishment = true
        });
    });

    options.AddPolicy("attack", httpContext =>
    {
        var key = httpContext.Items.TryGetValue("player_id", out var pid) ? pid?.ToString() : null;
        key ??= httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 20,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            TokensPerPeriod = 20,
            AutoReplenishment = true
        });
    });

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Bug #1: Friendly Vietnamese 429 body. OnRejected runs when the limiter
    // short-circuits — middleware registered after UseRateLimiter() is dead code
    // because the limiter never calls _next, so we must write the response here.
    options.OnRejected = async (ctx, _) =>
    {
        var http = ctx.HttpContext;
        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        http.Response.ContentType = "application/json";
        await http.Response.WriteAsJsonAsync(ApiError.Create(
            "RATE_LIMITED",
            "Quay nhanh quá, chờ xíu rồi bấm lại nha!"));
    };
});

builder.Services.AddDbContext<PkDbContext>(options =>
{
    var provider = (builder.Configuration["Storage:Provider"] ?? "sqlite").Trim().ToLowerInvariant();
    if (provider == "postgres")
    {
        var cs = builder.Configuration.GetConnectionString("Postgres");
        options.UseNpgsql(cs);
    }
    else
    {
        var cs = builder.Configuration.GetConnectionString("Sqlite");
        options.UseSqlite(cs);
    }
});

var cacheProvider = (builder.Configuration["Cache:Provider"] ?? "memory").Trim().ToLowerInvariant();
if (cacheProvider == "redis")
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = builder.Configuration.GetConnectionString("Redis");
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<EconomyService>();
builder.Services.AddScoped<SpinService>();
builder.Services.AddScoped<IslandService>();
builder.Services.AddScoped<AttackService>();
// Sprint-5: Event service (agent-11-event-service)
builder.Services.AddScoped<EventService>();

var app = builder.Build();

var migrateOnStartup = builder.Configuration.GetValue<bool?>("Database:MigrateOnStartup")
    ?? app.Environment.IsDevelopment();
if (migrateOnStartup)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PkDbContext>();
    // Nếu chưa có migration, dùng EnsureCreated; nếu có migration, dùng Migrate.
    var pendingMigrations = db.Database.GetPendingMigrations();
    if (pendingMigrations.Any())
    {
        db.Database.Migrate();
    }
    else
    {
        db.Database.EnsureCreated();
    }

    // r12: Ensure the Name column exists on Players table for existing DBs that
    // were created before the column was added. EnsureCreated does not add new
    // columns to an existing database, so we check and add it manually.
    try
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(Players)";
        using var reader = await cmd.ExecuteReaderAsync();
        var hasName = false;
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), "Name", StringComparison.OrdinalIgnoreCase))
            {
                hasName = true;
                break;
            }
        }
        await reader.CloseAsync();
        if (!hasName)
        {
            using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE Players ADD COLUMN Name TEXT NOT NULL DEFAULT 'Cướp biển ẩn danh'";
            await alterCmd.ExecuteNonQueryAsync();
        }
        await conn.CloseAsync();
    }
    catch { /* ignore — not SQLite or column exists */ }

    // Bug #4 (r4): seed/normalize the daily login event displayName to Vietnamese.
    // The event row is created out-of-band (manual insert / earlier seed), so this
    // idempotently upserts the Vietnamese display name for a fresh or existing DB
    // without requiring a migration. Runs after EnsureCreated/Migrate so the table
    // exists.
    await SeedDailyLoginEventAsync(db);
}

async Task SeedDailyLoginEventAsync(PkDbContext db)
{
    const string eventCode = "daily_login_bonus";
    const string displayNameVi = "Thưởng đăng nhập hằng ngày";

    var existing = await db.GameEvents.FirstOrDefaultAsync(e => e.EventCode == eventCode);
    if (existing == null)
    {
        // Fresh DB: create the event with the Vietnamese display name and a 1-year
        // active window so it shows up immediately for new installs.
        var now = DateTime.UtcNow;
        db.GameEvents.Add(new PK.Server.Domain.GameEvent
        {
            EventCode = eventCode,
            DisplayName = displayNameVi,
            EventType = "daily",
            StartAt = now,
            EndAt = now.AddYears(1),
            ConfigJson = "{\"reward\":\"gold_500\",\"reward_type\":\"gold\",\"reward_amount\":500}",
            IsActive = true
        });
        await db.SaveChangesAsync();
    }
    else if (existing.DisplayName != displayNameVi)
    {
        // Existing English name: normalize to Vietnamese.
        existing.DisplayName = displayNameVi;
        await db.SaveChangesAsync();
    }
}

app.UseMiddleware<RequestIdMiddleware>();
// ErrorHandlingMiddleware runs as early as possible (right after RequestIdMiddleware)
// so it can catch exceptions thrown by the auth/rate-limit/logging middleware below.
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<InternalAuthMiddleware>();
app.UseMiddleware<AuthMiddleware>();
// SpinBalanceMiddleware runs AFTER auth (needs player_id) but BEFORE rate limiting so
// that players with 0 spins receive a 409 NO_SPINS instead of a 429 with an empty body.
app.UseMiddleware<SpinBalanceMiddleware>();
app.UseRateLimiter();
app.UseMiddleware<LoggingScopeMiddleware>();
// RateLimitErrorMiddleware removed — OnRejected callback in AddRateLimiter above
// now handles the friendly 429 body, so this middleware was dead code.

// CORS must be applied before endpoints but after error handling so failures
// still carry CORS headers for browser clients to read.
app.UseCors("dev-permissive");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/healthz", () => Results.Ok(new { ok = true }))
    .WithName("Healthz")
    .WithOpenApi();

app.MapGet("/readyz", async (IServiceScopeFactory scopeFactory, CancellationToken ct) =>
    {
        // Simple DB connectivity check: can we open a connection and run a trivial query?
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PkDbContext>();
            var canConnect = await db.Database.CanConnectAsync(ct);
            if (!canConnect)
            {
                return Results.Json(new { ok = false, reason = "db_unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            return Results.Ok(new { ok = true });
        }
        catch (Exception)
        {
            return Results.Json(new { ok = false, reason = "db_error" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("Readyz")
    .WithOpenApi();

app.MapControllers();

app.Run();
