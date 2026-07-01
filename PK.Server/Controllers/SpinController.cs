using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PK.Server.Common;
using PK.Server.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace PK.Server.Controllers;

[Route("api/v1")]
public class SpinController : BaseApiController
{
    private readonly SpinService _spin;

    public SpinController(SpinService spin)
    {
        _spin = spin;
    }

    [HttpPost("spin")]
    [EnableRateLimiting("spin")]
    [SwaggerOperation(Summary = "Spin (RNG server-side)")]
    public async Task<IActionResult> Spin()
    {
        var playerId = TryGetPlayerId();
        if (playerId == null) return UnauthorizedError();

        var requestIdBad = TryRequireRequestId(out var requestId);
        if (requestIdBad != null) return requestIdBad;

        var result = await _spin.Spin(playerId.Value, requestId);
        if (!result.Success)
        {
            var code = result.ErrorCode ?? "INTERNAL";
            var status = code is "NO_SPINS" or "INSUFFICIENT_FUNDS" or "IDEMPOTENCY_KEY_CONFLICT" ? 409 : 400;
            return StatusCode(status, ApiError.Create(code, result.ErrorMessage ?? "Có lỗi xíu, thử lại nha!", result.ErrorDetails));
        }

        var r = result.Response!;
        // Build a result.amount field for schema compatibility. The detailed reward
        // remains under result.payload (gold|shield|spins|attack_token|raid_token).
        long? amount = r.Result.Payload.TryGetValue("gold", out var gold) ? (long?)gold
            : r.Result.Payload.TryGetValue("spins", out var spins) ? (long?)spins
            : r.Result.Payload.TryGetValue("shield", out var shield) ? (long?)shield
            : null;

        // Bug #3 (hardcore-r2): for attack/raid, do NOT fabricate a fake "100"
        // amount — the real reward is only known after the attack resolves, and
        // showing a fake number made players think they won gold they never got.
        // Leave amount as null (pending); the client shows "ATTACK! Bấm Attack để
        // cướp." instead of a fake gold amount.
        // (Previously this branch set amount = 100.)

        return Ok(new
        {
            spin_id = r.SpinId,
            result = new
            {
                type = r.Result.Type,
                amount,
                payload = r.Result.Payload
            },
            balances = r.Balances
        });
    }

    // Bug #2 (r4): daily free-spin reward endpoint. Grants 5 spins, idempotent per
    // UTC day per player (the SpinService derives a deterministic economy request
    // id from the player id + date, so repeated calls the same day don't double-grant).
    [HttpPost("spin/daily-reward")]
    [SwaggerOperation(Summary = "Claim daily free spins (5 spins, idempotent per day)")]
    public async Task<IActionResult> ClaimDailyReward()
    {
        var playerId = TryGetPlayerId();
        if (playerId == null) return UnauthorizedError();

        var result = await _spin.ClaimDailySpinReward(playerId.Value);
        if (!result.Success)
        {
            var code = result.ErrorCode ?? "INTERNAL";
            var status = code is "NO_SPINS" or "INSUFFICIENT_FUNDS" or "IDEMPOTENCY_KEY_CONFLICT" ? 409 : 400;
            return StatusCode(status, ApiError.Create(code, result.ErrorMessage ?? "Có lỗi xíu, thử lại nha!", result.ErrorDetails));
        }

        var r = result.Response!;
        return Ok(new
        {
            spins_granted = r.SpinsGranted,
            replayed = r.WasReplayed,
            balances = r.Balances
        });
    }
}
