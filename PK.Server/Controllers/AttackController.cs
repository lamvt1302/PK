using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PK.Server.Common;
using PK.Server.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace PK.Server.Controllers;

[Route("api/v1/attack")]
public class AttackController : BaseApiController
{
    private readonly AttackService _attacks;

    public AttackController(AttackService attacks)
    {
        _attacks = attacks;
    }

    [HttpPost("start")]
    [EnableRateLimiting("attack")]
    [SwaggerOperation(Summary = "Start attack (pick target)")]
    public async Task<IActionResult> Start()
    {
        var playerId = TryGetPlayerId();
        if (playerId == null) return UnauthorizedError();

        var requestIdBad = TryRequireRequestId(out var requestId);
        if (requestIdBad != null) return requestIdBad;

        var result = await _attacks.Start(playerId.Value, requestId);
        if (!result.Success)
        {
            return StatusCode(400, ApiError.Create(result.ErrorCode ?? "INTERNAL", result.ErrorMessage ?? "Có lỗi xíu, thử lại nha!", result.ErrorDetails));
        }

        var r = result.Response!;
        return Ok(new
        {
            attack_session_id = r.AttackSessionId,
            target = new
            {
                player_id = r.Target.PlayerId,
                name = r.Target.Name,
                current_island = r.Target.CurrentIsland,
                shield_count = r.Target.ShieldCount
            }
        });
    }

    public sealed record ResolveRequest(string attack_session_id, object? client_input);

    [HttpPost("resolve")]
    [EnableRateLimiting("attack")]
    [SwaggerOperation(Summary = "Resolve attack (server-authoritative payout)")]
    public async Task<IActionResult> Resolve([FromBody] ResolveRequest? req)
    {
        var playerId = TryGetPlayerId();
        if (playerId == null) return UnauthorizedError();

        var requestIdBad = TryRequireRequestId(out var requestId);
        if (requestIdBad != null) return requestIdBad;

        // Validate attack_session_id explicitly so malformed input returns a clean ApiError
        // instead of leaking internal type information (e.g. controller+ResolveRequest).
        // Bug #3: a missing/blank session id returns NO_ATTACK_SESSION with a friendly
        // Vietnamese message, since that's the common case (player tapped Resolve
        // without an active attack). A present-but-malformed value still returns
        // INVALID_ARGUMENT.
        if (req == null || string.IsNullOrWhiteSpace(req.attack_session_id))
        {
            return BadRequest(ApiError.Create(
                "NO_ATTACK_SESSION",
                "Chưa có phiên tấn công nào. Bấm Attack trước nha!"));
        }

        if (!Guid.TryParse(req.attack_session_id, out var attackSessionId))
        {
            return BadRequest(ApiError.Create("INVALID_ARGUMENT", "Phiên tấn công không hợp lệ"));
        }

        var result = await _attacks.Resolve(playerId.Value, requestId, attackSessionId, req.client_input);
        if (!result.Success)
        {
            // ATTACK_ALREADY_RESOLVED is a conflict (409); other validation errors are 400.
            var code = result.ErrorCode ?? "INTERNAL";
            var status = code == "ATTACK_ALREADY_RESOLVED" ? 409 : 400;
            return StatusCode(status, ApiError.Create(code, result.ErrorMessage ?? "Có lỗi xíu, thử lại nha!", result.ErrorDetails));
        }

        var r = result.Response!;
        return Ok(new
        {
            result = new
            {
                success = r.Result.Success,
                gold_stolen = r.Result.GoldStolen,
                shield_consumed = r.Result.ShieldConsumed,
                chest_multiplier = r.Result.ChestMultiplier
            },
            // Top-level gold_stolen for spec consistency (also available under result.gold_stolen).
            gold_stolen = r.Result.GoldStolen,
            balances = r.Balances
        });
    }
}
