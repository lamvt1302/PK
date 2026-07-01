using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PK.Server.Common;
using PK.Server.Data;
using PK.Server.Domain;

namespace PK.Server.Services
{
    /// <summary>
    /// Event service — manages active events, claim flow, config-driven rewards.
    /// Agent-11-event-service
    /// </summary>
    public class EventService
    {
        private readonly PkDbContext _db;
        private readonly EconomyService _economy;
        private readonly ILogger<EventService> _logger;

        public EventService(PkDbContext db, EconomyService economy, ILogger<EventService> logger)
        {
            _db = db;
            _economy = economy;
            _logger = logger;
        }

        /// <summary>
        /// Get all active events (within date range + IsActive=true).
        /// Bug #6: excludes events the requesting player has already claimed, so
        /// a claimed event no longer shows up in the active list (which previously
        /// tempted players to try claiming it again).
        /// </summary>
        public async Task<List<GameEvent>> GetActiveEventsAsync(Guid playerId, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            // Left-anti-join against PlayerEventClaims for this player so already-
            // claimed events are filtered out.
            var claimedEventIds = await _db.PlayerEventClaims
                .Where(c => c.PlayerId == playerId && c.Status == "claimed")
                .Select(c => c.EventId)
                .ToListAsync(ct);

            return await _db.GameEvents
                .Where(e => e.IsActive && e.StartAt <= now && e.EndAt >= now)
                .Where(e => !claimedEventIds.Contains(e.Id))
                .OrderBy(e => e.StartAt)
                .ToListAsync(ct);
        }

        /// <summary>
        /// Get a specific event by code.
        /// </summary>
        public async Task<GameEvent?> GetEventByCodeAsync(string eventCode, CancellationToken ct = default)
        {
            return await _db.GameEvents
                .FirstOrDefaultAsync(e => e.EventCode == eventCode && e.IsActive, ct);
        }

        /// <summary>
        /// Get a specific event by id. Bug #7: used by the claim endpoint to read
        /// the event config so the reward amount can be returned to the client.
        /// </summary>
        public async Task<GameEvent?> GetEventByIdAsync(int eventId, CancellationToken ct = default)
        {
            return await _db.GameEvents
                .FirstOrDefaultAsync(e => e.Id == eventId, ct);
        }

        /// <summary>
        /// Claim event reward for a player.
        /// Idempotent: if already claimed, returns existing claim.
        /// Wrapped in a transaction with a DbUpdateException handler to safely
        /// replay an idempotent claim if a concurrent insert beat us (same pattern as SpinService).
        /// </summary>
        public async Task<PlayerEventClaim> ClaimRewardAsync(
            Guid playerId,
            int eventId,
            string claimDataJson,
            CancellationToken ct = default)
        {
            // Idempotency: check existing claim (fast path, no transaction)
            var existing = await _db.PlayerEventClaims
                .FirstOrDefaultAsync(c => c.PlayerId == playerId && c.EventId == eventId, ct);

            if (existing != null)
            {
                _logger.LogInformation("Event {EventId} already claimed by player {PlayerId}", eventId, playerId);
                return existing;
            }

            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            // Re-check under transaction to avoid races
            existing = await _db.PlayerEventClaims
                .FirstOrDefaultAsync(c => c.PlayerId == playerId && c.EventId == eventId, ct);

            if (existing != null)
            {
                await tx.CommitAsync(ct);
                _logger.LogInformation("Event {EventId} already claimed by player {PlayerId} (re-check)", eventId, playerId);
                return existing;
            }

            // Verify event is active
            var evt = await _db.GameEvents.FindAsync(new object[] { eventId }, ct);
            if (evt == null || !evt.IsActive)
            {
                await tx.RollbackAsync(ct);
                throw new InvalidOperationException("Sự kiện không tồn tại hoặc đã kết thúc");
            }

            var now = DateTime.UtcNow;
            if (now < evt.StartAt || now > evt.EndAt)
            {
                await tx.RollbackAsync(ct);
                throw new InvalidOperationException("Sự kiện chưa mở hoặc đã hết hạn");
            }

            var claim = new PlayerEventClaim
            {
                PlayerId = playerId,
                EventId = eventId,
                Status = "claimed",
                ClaimDataJson = claimDataJson,
                ClaimedAt = now
            };

            _db.PlayerEventClaims.Add(claim);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                await tx.RollbackAsync(ct);
                // A concurrent insert likely beat us — replay the idempotent claim.
                existing = await _db.PlayerEventClaims
                    .FirstOrDefaultAsync(c => c.PlayerId == playerId && c.EventId == eventId, ct);
                if (existing != null)
                {
                    _logger.LogInformation("Event {EventId} claim replayed after DbUpdateException for player {PlayerId}", eventId, playerId);
                    return existing;
                }
                throw;
            }

            // Bug #2 (hardcore-r2): actually GRANT the configured reward to the
            // player's balance on claim. Previously the claim only recorded a row
            // but never credited any gold, so the player saw "Nhận được 0 vàng".
            // We parse the reward from the event config (e.g. "gold_500" -> 500
            // gold) and apply an idempotent ADD_GOLD economy transaction keyed off
            // the claim id so retries don't double-credit.
            var (rewardType, rewardAmount) = ExtractReward(evt.ConfigJson);
            if (rewardAmount > 0 && rewardType == "gold")
            {
                // Deterministic id derived from the claim id so a replayed claim
                // (e.g. after a transient failure) credits the gold exactly once.
                // claim.Id is an int, so wrap it into a Guid for DeterministicGuid.
                var claimGuid = new Guid(claim.Id, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
                var rewardTxId = DeterministicGuid.From(claimGuid, "event-claim-reward-gold");
                var add = await _economy.ApplyTransaction(playerId, rewardTxId, "ADD_GOLD", rewardAmount, "EVENT_CLAIM_REWARD");
                if (!add.Success && !add.WasReplayed)
                {
                    await tx.RollbackAsync(ct);
                    throw new InvalidOperationException($"Không thể nhận thưởng sự kiện: {add.ErrorMessage}");
                }
            }

            await tx.CommitAsync(ct);

            _logger.LogInformation("Event {EventId} claimed by player {PlayerId} at {ClaimedAt} (reward: {RewardType} {RewardAmount})",
                eventId, playerId, now, rewardType, rewardAmount);

            return claim;
        }

        /// <summary>
        /// Bug #2 (hardcore-r2): extracts the reward type and amount from the event
        /// config JSON. Handles the compact "gold_500" / "spins_10" format where the
        /// reward value is a string of the form "&lt;type&gt;_&lt;number&gt;", as
        /// well as the direct numeric keys "reward_amount", "gold", and "amount".
        /// Returns ("gold", 0) when nothing can be parsed.
        /// </summary>
        internal static (string type, long amount) ExtractReward(string? configJson)
        {
            var type = "gold";
            long amount = 0;
            if (string.IsNullOrWhiteSpace(configJson))
            {
                return (type, amount);
            }

            try
            {
                using var doc = JsonDocument.Parse(configJson);
                var root = doc.RootElement;

                // 1) Direct numeric "reward_amount".
                if (root.TryGetProperty("reward_amount", out var ra) && ra.TryGetInt64(out var a1))
                {
                    amount = a1;
                }
                // 2) Direct numeric "gold".
                else if (root.TryGetProperty("gold", out var g) && g.TryGetInt64(out var a2))
                {
                    amount = a2;
                }
                // 3) Direct numeric "amount".
                else if (root.TryGetProperty("amount", out var am) && am.TryGetInt64(out var a3))
                {
                    amount = a3;
                }

                // 4) Explicit "reward_type" string overrides the default type.
                if (root.TryGetProperty("reward_type", out var rt) && rt.ValueKind == JsonValueKind.String)
                {
                    var rtStr = rt.GetString();
                    if (!string.IsNullOrWhiteSpace(rtStr)) type = rtStr;
                }

                // 5) Compact typed reward string under "reward", e.g. "gold_500" ->
                //    type="gold", amount=500. Only parse this when no direct numeric
                //    amount was found above.
                if (amount <= 0 && root.TryGetProperty("reward", out var rw))
                {
                    if (rw.ValueKind == JsonValueKind.String)
                    {
                        var rwStr = rw.GetString();
                        if (!string.IsNullOrWhiteSpace(rwStr))
                        {
                            var (t, a) = ParseTypedReward(rwStr);
                            if (a > 0)
                            {
                                type = t;
                                amount = a;
                            }
                        }
                    }
                    else if (rw.ValueKind == JsonValueKind.Number && rw.TryGetInt64(out var a4))
                    {
                        amount = a4;
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore malformed config; keep defaults.
            }

            return (type, amount);
        }

        /// <summary>
        /// Bug #2 (hardcore-r2): parses a typed reward string like "gold_500" or
        /// "spins_10" into (type, amount). Returns ("gold", 0) for a pure-numeric
        /// string and ("gold", 0) when the string doesn't end in a number.
        /// </summary>
        private static (string type, long amount) ParseTypedReward(string value)
        {
            var v = value.Trim();
            int underscore = v.LastIndexOf('_');
            if (underscore > 0 && underscore + 1 < v.Length)
            {
                var typePart = v.Substring(0, underscore);
                var numPart = v.Substring(underscore + 1);
                if (long.TryParse(numPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
                {
                    return (string.IsNullOrWhiteSpace(typePart) ? "gold" : typePart, n);
                }
            }
            // Pure-numeric string -> treat as gold amount.
            if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var direct) && direct > 0)
            {
                return ("gold", direct);
            }
            return ("gold", 0);
        }

        /// <summary>
        /// Get claim history for a player.
        /// </summary>
        public async Task<List<PlayerEventClaim>> GetPlayerClaimsAsync(
            Guid playerId, CancellationToken ct = default)
        {
            return await _db.PlayerEventClaims
                .Include(c => c.Event)
                .Where(c => c.PlayerId == playerId)
                .OrderByDescending(c => c.ClaimedAt)
                .ToListAsync(ct);
        }
    }
}