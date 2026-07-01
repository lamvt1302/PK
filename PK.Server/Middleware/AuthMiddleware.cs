using PK.Server.Services;

namespace PK.Server.Middleware;

/// <summary>
/// MVP auth đơn giản: Bearer token ↔ player_id lưu trong Redis (IDistributedCache).
/// </summary>
public class AuthMiddleware
{
    private readonly RequestDelegate _next;

    public AuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context, AuthService auth)
    {
        // Public routes (no auth)
        if (context.Request.Path.StartsWithSegments("/swagger")
            || context.Request.Path.StartsWithSegments("/healthz")
            || context.Request.Path.StartsWithSegments("/readyz")
            || context.Request.Path.StartsWithSegments("/api/v1/player/guest-login"))
        {
            await _next(context);
            return;
        }

        var token = ExtractBearer(context.Request.Headers.Authorization);
        if (!string.IsNullOrWhiteSpace(token))
        {
            var playerId = await auth.TryGetPlayerIdByToken(token);
            if (playerId != null)
            {
                context.Items["player_id"] = playerId.Value;
            }
        }

        await _next(context);
    }

    private static string? ExtractBearer(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization)) return null;
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        return authorization.Substring(prefix.Length).Trim();
    }
}

