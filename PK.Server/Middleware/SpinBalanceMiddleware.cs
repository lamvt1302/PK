using Microsoft.EntityFrameworkCore;
using PK.Server.Common;
using PK.Server.Data;

namespace PK.Server.Middleware;

/// <summary>
/// Pre-checks the spin balance for POST /api/v1/spin BEFORE the rate limiter runs.
/// Without this, a player who has exhausted their spins (spins == 0) and keeps
/// retrying would receive a 429 (rate limited, empty body) once they exceed the
/// token-bucket limit, instead of the correct 409 NO_SPINS business error.
///
/// The middleware only short-circuits the /spin endpoint when the authenticated
/// player's spin balance is already 0; otherwise it lets the request through so
/// the normal rate-limiting + idempotency + RNG flow applies.
/// </summary>
public class SpinBalanceMiddleware
{
    private readonly RequestDelegate _next;

    public SpinBalanceMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        // Only intercept the spin endpoint — NOT /spin/daily-reward which must
        // work even when spins=0 (it grants spins, not consumes them).
        if (HttpMethods.IsPost(context.Request.Method)
            && context.Request.Path == "/api/v1/spin")
        {
            // Require an authenticated player_id (set by AuthMiddleware which runs earlier).
            if (context.Items.TryGetValue("player_id", out var pidObj) && pidObj is Guid playerId)
            {
                var db = context.RequestServices.GetRequiredService<PkDbContext>();
                var player = await db.Players.FirstOrDefaultAsync(x => x.Id == playerId);
                // If the player exists and has no spins, short-circuit with 409 NO_SPINS
                // before the rate limiter can return a 429 with an empty body.
                if (player != null && player.Spins <= 0)
                {
                    context.Response.StatusCode = StatusCodes.Status409Conflict;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(
                        ApiError.Create("NO_SPINS", "Hết lượt quay!", new { current = player.Spins }));
                    return;
                }
            }
        }

        await _next(context);
    }
}