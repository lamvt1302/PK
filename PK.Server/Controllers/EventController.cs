using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using PK.Server.Common;
using PK.Server.Services;

namespace PK.Server.Controllers
{
    /// <summary>
    /// Event API — active events + claim rewards.
    /// Agent-11-event-service
    /// </summary>
    [ApiController]
    [Route("api/v1/events")]
    public class EventController : BaseApiController
    {
        private readonly EventService _eventService;

        public EventController(EventService eventService)
        {
            _eventService = eventService;
        }

        /// <summary>
        /// Get all active events.
        /// </summary>
        [HttpGet("active")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetActiveEvents(CancellationToken ct)
        {
            var playerId = TryGetPlayerId();
            if (playerId == null)
                return UnauthorizedError();

            // Bug #6: pass the player id so already-claimed events are excluded.
            var events = await _eventService.GetActiveEventsAsync(playerId.Value, ct);
            return Ok(events.Select(e => new
            {
                id = e.Id,
                eventCode = e.EventCode,
                displayName = e.DisplayName,
                eventType = e.EventType,
                startAt = e.StartAt,
                endAt = e.EndAt,
                // Serialize config as a nested object, not a double-encoded JSON string.
                config = ParseConfig(e.ConfigJson)
            }));
        }

        /// <summary>
        /// Claim event reward.
        /// </summary>
        [HttpPost("claim")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> ClaimReward([FromBody] ClaimRequest req, CancellationToken ct)
        {
            var playerId = TryGetPlayerId();
            if (playerId == null)
                return UnauthorizedError();

            if (req.EventId == 0)
                return BadRequest(ApiError.Create("INVALID_ARGUMENT", "event_id là bắt buộc"));

            try
            {
                // Check if player already claimed this event — return 409 so the
                // client can show "Đã nhận rồi!" instead of silently re-claiming.
                var existingClaims = await _eventService.GetPlayerClaimsAsync(playerId.Value, ct);
                if (existingClaims.Any(c => c.EventId == req.EventId && c.Status == "claimed"))
                {
                    return StatusCode(StatusCodes.Status409Conflict, ApiError.Create(
                        "EVENT_ALREADY_CLAIMED",
                        "Sự kiện này đã được nhận rồi!"));
                }

                var claim = await _eventService.ClaimRewardAsync(playerId.Value, req.EventId, req.ClaimData ?? "{}", ct);

                // Bug #2 (hardcore-r2): surface the actual reward amount in a clear
                // field so the client can show "Nhận được 500 vàng!". Use the shared
                // EventService.ExtractReward helper so the controller and service
                // parse the config identically (handles "gold_500" compact format
                // as well as direct numeric "reward_amount"/"gold"/"amount" keys).
                var evt = await _eventService.GetEventByIdAsync(claim.EventId, ct);
                var (rewardType, rewardAmount) = EventService.ExtractReward(evt?.ConfigJson);

                return Ok(new
                {
                    claimId = claim.Id,
                    eventId = claim.EventId,
                    status = claim.Status,
                    claimedAt = claim.ClaimedAt,
                    // Bug #2/#7: explicit reward fields for the client.
                    reward_type = rewardType,
                    reward_amount = rewardAmount
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiError.Create("EVENT_CLAIM_FAILED", ex.Message));
            }
        }

        // NOTE: The reward extraction logic now lives in EventService.ExtractReward
        // (Bug #2 hardcore-r2) so both the claim flow and the response use the same
        // parser. The previous private ExtractReward helper here has been removed
        // to avoid drift between the two implementations.

        /// <summary>
        /// Get player's claim history.
        /// </summary>
        [HttpGet("claims")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> GetClaims(CancellationToken ct)
        {
            var playerId = TryGetPlayerId();
            if (playerId == null)
                return UnauthorizedError();

            var claims = await _eventService.GetPlayerClaimsAsync(playerId.Value, ct);
            return Ok(claims.Select(c => new
            {
                claimId = c.Id,
                eventId = c.EventId,
                eventCode = c.Event?.EventCode,
                status = c.Status,
                claimedAt = c.ClaimedAt
            }));
        }

        public class ClaimRequest
        {
            // Accept both the snake_case wire form ("event_id") used by the docs /
            // non-Unity clients AND the camelCase form ("eventId") emitted by
            // Unity's JsonUtility. The snake_case form is wired with an explicit
            // [JsonPropertyName]; the camelCase form is wired through a lowercase
            // delegate property whose own default JSON name is exactly "eventId".
            //
            // Why not put [JsonPropertyName] on the camel alias too:
            // System.Text.Json treats every [JsonPropertyName] plus every default
            // property name as a candidate JSON member (case-insensitively folded).
            // The previous version had [JsonPropertyName("claimData")] on an alias
            // property AND a bare "ClaimData" property. After folding, BOTH resolved
            // to the name "claimData", so the serializer threw
            //   "The JSON property name for 'ClaimRequest.ClaimData' collides with
            //    another property."
            // for EVERY request body. That InvalidOperationException escaped the
            // model binder and was caught by ErrorHandlingMiddleware, which returned
            // the generic 500 INTERNAL "Lỗi server, thử lại nha!" — masking the
            // real cause from the client.
            //
            // Fix: only one [JsonPropertyName] per logical field. The camelCase
            // forms ("eventId", "claimData") are handled by lowercase delegate
            // properties whose default JSON names are exactly those camelCase
            // strings, so they bind without any attribute and therefore cannot
            // collide with the snake_case [JsonPropertyName] on the main property.

            [JsonPropertyName("event_id")]
            public int EventId { get; set; }
            // Binds the wire key "eventId" (Unity JsonUtility) to the same EventId.
            public int eventId { get => EventId; set => EventId = value; }

            [JsonPropertyName("claim_data")]
            public string? ClaimData { get; set; }
            // Binds the wire key "claimData" (Unity JsonUtility) to the same ClaimData.
            public string? claimData { get => ClaimData; set => ClaimData = value; }
        }

        /// <summary>
        /// Parses the event ConfigJson string into a JsonElement so it is serialized
        /// as a nested object rather than a double-encoded JSON string. Falls back to
        /// an empty object when the stored config is null/blank/invalid.
        /// </summary>
        private static JsonElement ParseConfig(string? configJson)
        {
            if (string.IsNullOrWhiteSpace(configJson))
            {
                return JsonSerializer.Deserialize<JsonElement>("{}");
            }

            try
            {
                return JsonSerializer.Deserialize<JsonElement>(configJson);
            }
            catch (JsonException)
            {
                return JsonSerializer.Deserialize<JsonElement>("{}");
            }
        }
    }
}