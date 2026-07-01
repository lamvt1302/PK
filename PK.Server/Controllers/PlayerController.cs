using Microsoft.AspNetCore.Mvc;
using PK.Server.Common;
using PK.Server.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace PK.Server.Controllers;

[Route("api/v1/player")]
public class PlayerController : BaseApiController
{
    private readonly PlayerService _players;
    private readonly AuthService _auth;

    public PlayerController(PlayerService players, AuthService auth)
    {
        _players = players;
        _auth = auth;
    }

    public sealed record GuestLoginRequest(string device_id);

    [HttpPost("guest-login")]
    [SwaggerOperation(Summary = "Guest login (tạo hoặc load player theo device_id)")]
    public async Task<IActionResult> GuestLogin([FromBody] GuestLoginRequest req)
    {
        var deviceId = (req.device_id ?? string.Empty).Trim();
        if (deviceId.Length is < 8 or > 128)
        {
            return BadRequest(ApiError.Create("INVALID_DEVICE_ID", "device_id không hợp lệ", new { rule = "length 8..128" }));
        }

        var player = await _players.GetOrCreateGuestPlayer(deviceId);
        var token = await _auth.IssueGuestToken(player.Id);

        // MVP note: guest login returns the persisted player state (gold, spins,
        // shield_count, etc.) rather than a fresh/anonymous session. This is
        // acceptable for the MVP because device_id is the idempotency key and the
        // player's progress is meant to persist across sessions on the same device.
        return Ok(new
        {
            player_id = player.Id,
            token,
            profile = new
            {
                level = player.Level,
                xp = player.Xp,
                gold = player.Gold,
                spins = player.Spins,
                shield_count = player.ShieldCount,
                current_island = player.CurrentIsland
            }
        });
    }

    [HttpGet("profile")]
    [SwaggerOperation(Summary = "Get player profile")]
    public async Task<IActionResult> GetProfile()
    {
        var playerId = TryGetPlayerId();
        if (playerId == null) return UnauthorizedError();

        var player = await _players.GetPlayer(playerId.Value);
        if (player == null) return NotFound(ApiError.Create("PLAYER_NOT_FOUND", "Không tìm thấy người chơi"));

        return Ok(new
        {
            player_id = player.Id,
            level = player.Level,
            xp = player.Xp,
            gold = player.Gold,
            spins = player.Spins,
            shield_count = player.ShieldCount,
            current_island = player.CurrentIsland
        });
    }
}
