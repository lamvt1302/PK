using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PK.Server.Common;
using PK.Server.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace PK.Server.Controllers;

[Route("api/v1/island")]
public class IslandController : BaseApiController
{
    private readonly IslandService _islands;

    public IslandController(IslandService islands)
    {
        _islands = islands;
    }

    [HttpGet("state")]
    [SwaggerOperation(Summary = "Get island state")]
    public async Task<IActionResult> GetState()
    {
        var playerId = TryGetPlayerId();
        if (playerId == null) return UnauthorizedError();

        var state = await _islands.GetIslandState(playerId.Value);
        return Ok(new
        {
            current_island = state.CurrentIsland,
            buildings = state.Buildings.Select(b => new { slot = b.Slot, level = b.Level })
        });
    }

    public sealed record UpgradeRequest(int slot);

    [HttpPost("upgrade")]
    [EnableRateLimiting("upgrade")]
    [SwaggerOperation(Summary = "Upgrade building (cost via Economy)")]
    public async Task<IActionResult> Upgrade([FromBody] UpgradeRequest req)
    {
        var playerId = TryGetPlayerId();
        if (playerId == null) return UnauthorizedError();

        var requestIdBad = TryRequireRequestId(out var requestId);
        if (requestIdBad != null) return requestIdBad;

        var result = await _islands.Upgrade(playerId.Value, requestId, req.slot);
        if (!result.Success)
        {
            var code = result.ErrorCode ?? "INTERNAL";
            var status = code == "INSUFFICIENT_FUNDS" ? 409 : 400;
            return StatusCode(status, ApiError.Create(code, result.ErrorMessage ?? "Có lỗi xíu, thử lại nha!", result.ErrorDetails));
        }

        var r = result.Response!;
        var buildingObj = new { slot = r.Upgraded.Slot, level = r.Upgraded.Level };
        return Ok(new
        {
            // Keep "upgraded" for backwards compat; add "building" alias per spec.
            upgraded = buildingObj,
            building = buildingObj,
            balances = r.Balances,
            island = r.Island
        });
    }
}
