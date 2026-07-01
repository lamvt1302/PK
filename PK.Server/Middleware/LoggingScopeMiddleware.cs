namespace PK.Server.Middleware;

public class LoggingScopeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<LoggingScopeMiddleware> _logger;

    public LoggingScopeMiddleware(RequestDelegate next, ILogger<LoggingScopeMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var requestId = context.Items.TryGetValue("X-Request-Id", out var rid) ? rid?.ToString() : null;
        var playerId = context.Items.TryGetValue("player_id", out var pid) ? pid?.ToString() : null;

        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["request_id"] = requestId,
            ["player_id"] = playerId,
            ["path"] = context.Request.Path.ToString(),
            ["method"] = context.Request.Method
        }))
        {
            var start = DateTime.UtcNow;
            await _next(context);
            var latencyMs = (DateTime.UtcNow - start).TotalMilliseconds;
            _logger.LogInformation("Request completed {status_code} {latency_ms}ms", context.Response.StatusCode, (int)latencyMs);
        }
    }
}

