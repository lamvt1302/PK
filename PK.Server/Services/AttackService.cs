using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PK.Server.Common;
using PK.Server.Data;
using PK.Server.Domain;

namespace PK.Server.Services;

public class AttackService
{
    private readonly PkDbContext _db;
    private readonly EconomyService _economy;

    public AttackService(PkDbContext db, EconomyService economy)
    {
        _db = db;
        _economy = economy;
    }

    public async Task<AttackStartResult> Start(Guid attackerPlayerId, Guid requestId)
    {
        // Idempotent: nếu đã có session tạo bởi requestId, trả lại.
        var existed = await _db.AttackSessions.FirstOrDefaultAsync(x => x.AttackerPlayerId == attackerPlayerId && x.StartRequestId == requestId);
        if (existed != null)
        {
            var target = await _db.Players.FirstAsync(x => x.Id == existed.TargetPlayerId);
            return AttackStartResult.Replay(new AttackStartResponse(
                AttackSessionId: existed.Id,
                Target: new AttackTarget(target.Id, target.Name, target.CurrentIsland, target.ShieldCount)
            ));
        }

        var targetPlayer = await PickTarget(attackerPlayerId);
        if (targetPlayer == null) return AttackStartResult.Error("TARGET_NOT_FOUND", "Không tìm thấy mục tiêu tấn công", new { });

        var session = new AttackSession
        {
            AttackerPlayerId = attackerPlayerId,
            TargetPlayerId = targetPlayer.Id,
            StartRequestId = requestId,
            Status = "STARTED",
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.AttackSessions.Add(session);
        await _db.SaveChangesAsync();

        return AttackStartResult.Applied(new AttackStartResponse(
            AttackSessionId: session.Id,
            Target: new AttackTarget(targetPlayer.Id, targetPlayer.Name, targetPlayer.CurrentIsland, targetPlayer.ShieldCount)
        ));
    }

    public async Task<AttackResolveResult> Resolve(Guid attackerPlayerId, Guid requestId, Guid attackSessionId, object? clientInput)
    {
        // Bug #6 (r16): parse optional chest_index from client_input for the raid
        // mini-game. When present (0-2), the server applies a per-chest multiplier
        // to the rolled gold payout. The multiplier assignment is a deterministic
        // shuffle seeded by the attack session id, so the player must guess which
        // chest hides the 2x jackpot — server-authoritative, no trust needed.
        int? chestIndex = null;
        if (clientInput is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            if (je.TryGetProperty("chest_index", out var ci) && ci.ValueKind == JsonValueKind.Number && ci.TryGetInt32(out var ciVal))
            {
                if (ciVal >= 0 && ciVal <= 2) chestIndex = ciVal;
            }
        }

        var existed = await _db.AttackLogs.FirstOrDefaultAsync(x => x.AttackerPlayerId == attackerPlayerId && x.RequestId == requestId);
        if (existed != null)
        {
            var balances = JsonSerializer.Deserialize<Dictionary<string, object?>>(existed.BalancesAfterJson) ?? new();
            return AttackResolveResult.Replay(new AttackResolveResponse(
                Result: new AttackResult(existed.Success, existed.GoldStolen, existed.ShieldConsumed, 1f),
                Balances: balances
            ));
        }

        await using var tx = await _db.Database.BeginTransactionAsync();

        var session = await _db.AttackSessions.FirstOrDefaultAsync(x => x.Id == attackSessionId);
        if (session == null) return AttackResolveResult.Error("INVALID_ARGUMENT", "Phiên tấn công không hợp lệ", new { attack_session_id = attackSessionId });
        if (session.AttackerPlayerId != attackerPlayerId) return AttackResolveResult.Error("FORBIDDEN", "Đây không phải phiên tấn công của bạn", new { });
        if (string.Equals(session.Status, "RESOLVED", StringComparison.OrdinalIgnoreCase))
        {
            // Double-resolve with a different requestId is a conflict (409), not a silent replay.
            // The idempotent replay path (same requestId) is already handled at the top of this method.
            await tx.RollbackAsync();
            return AttackResolveResult.Error("ATTACK_ALREADY_RESOLVED", "Phiên tấn công đã kết thúc rồi", new { attack_session_id = attackSessionId });
        }

        var attacker = await _db.Players.FirstAsync(x => x.Id == attackerPlayerId);
        var target = await _db.Players.FirstAsync(x => x.Id == session.TargetPlayerId);

        var shieldConsumed = false;
        long goldStolen = 0;
        var success = false;
        // Bug #6 (r16): chest multiplier for the raid mini-game. Default 1x for
        // plain attacks / shield-blocked raids; only set to >1 when a valid
        // chest_index was supplied and the target had no shield.
        float multiplier = 1f;

        // MVP rule tối thiểu theo game design v1: nếu target có shield -> consume 1 và không mất gold.
        if (target.ShieldCount > 0)
        {
            target.ShieldCount -= 1;
            target.UpdatedAt = DateTimeOffset.UtcNow;
            shieldConsumed = true;
            success = false;
            goldStolen = 0;
            await _db.SaveChangesAsync();
        }
        else
        {
            success = true;
            // Bug #1 (r4): attack reward was a flat 120 cap (often only ~8 gold in
            // practice), which felt pointless next to spin rewards (50-600). Now
            // roll a significant payout (200-500). If the target has less gold
            // than the roll, steal what they have — but guarantee a minimum payout
            // of 200 so attacks always feel rewarding (the house covers the gap).
            //
            // Bug #3 (r7): trước đây dùng
            //   goldStolen = Math.Max(200, Math.Min(roll, target.Gold));
            // Khi target.Gold thấp (vd 0), Math.Min(roll, 0) = 0 rồi Math.Max(200,0)
            // = 200 -> goldStolen LUÔN bằng 200 bất chấp roll. Vì code phía dưới đã
            // mint phần deficit khi target.Gold < goldStolen, ta cứ dùng nguyên roll
            // (200-500) để phần thưởng ngẫu nhiên như thiết kế.
            var roll = Random.Shared.Next(200, 501);
            // Bug #6 (r16): raid mini-game chest multiplier. The three chests are
            // assigned multipliers {1x, 1.5x, 2x} via a deterministic shuffle seeded
            // by the attack session id. If the client supplied a chest_index, the
            // rolled gold is multiplied by that chest's factor (rounded). If no
            // chest_index was supplied (plain attack path, or older clients), the
            // multiplier is 1x and the behaviour is unchanged.
            if (chestIndex.HasValue)
            {
                var seed = attackSessionId.GetHashCode();
                var mults = new[] { 1f, 1.5f, 2f };
                // Fisher-Yates shuffle seeded by the session id so the jackpot
                // chest position is unpredictable but deterministic per session.
                var rng = new System.Random(seed);
                for (int i = mults.Length - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (mults[i], mults[j]) = (mults[j], mults[i]);
                }
                multiplier = mults[chestIndex.Value];
            }
            goldStolen = (long)System.Math.Round(roll * multiplier);
            if (goldStolen > 0)
            {
                // If the target doesn't have enough gold, the attacker still gets
                // the guaranteed minimum (200) — the deficit is minted rather than
                // stolen from an empty target.
                if (target.Gold < goldStolen)
                {
                    // Steal what the target has, mint the rest.
                    var deficit = goldStolen - target.Gold;
                    if (target.Gold > 0)
                    {
                        var transferReal = await _economy.TransferGold(requestId, fromPlayerId: target.Id, toPlayerId: attacker.Id, amount: target.Gold, reason: "ATTACK_PAYOUT");
                        if (!transferReal.ok)
                        {
                            await tx.RollbackAsync();
                            return AttackResolveResult.Error(transferReal.errorCode ?? "INTERNAL", transferReal.errorMessage ?? "Có lỗi xíu, thử lại nha!", new { });
                        }
                    }
                    // Mint the deficit directly to the attacker.
                    if (deficit > 0)
                    {
                        var mint = await _economy.ApplyTransaction(attacker.Id, DeterministicGuid.From(requestId, "attack-mint"), "ADD_GOLD", deficit, "ATTACK_BONUS");
                        if (!mint.Success && !mint.WasReplayed)
                        {
                            await tx.RollbackAsync();
                            return AttackResolveResult.Error(mint.ErrorCode ?? "INTERNAL", mint.ErrorMessage ?? "Có lỗi xíu, thử lại nha!", new { });
                        }
                    }
                }
                else
                {
                    var transfer = await _economy.TransferGold(requestId, fromPlayerId: target.Id, toPlayerId: attacker.Id, amount: goldStolen, reason: "ATTACK_PAYOUT");
                    if (!transfer.ok)
                    {
                        await tx.RollbackAsync();
                        return AttackResolveResult.Error(transfer.errorCode ?? "INTERNAL", transfer.errorMessage ?? "Có lỗi xíu, thử lại nha!", new { });
                    }
                }
            }
        }

        session.Status = "RESOLVED";
        await _db.SaveChangesAsync();

        // refresh attacker state
        attacker = await _db.Players.FirstAsync(x => x.Id == attackerPlayerId);
        var balancesAfter = new Dictionary<string, object?>
        {
            ["gold"] = attacker.Gold,
            ["spins"] = attacker.Spins,
            ["shield_count"] = attacker.ShieldCount
        };

        var log = new AttackLog
        {
            AttackSessionId = attackSessionId,
            AttackerPlayerId = attackerPlayerId,
            TargetPlayerId = target.Id,
            RequestId = requestId,
            ClientInputJson = JsonSerializer.Serialize(clientInput),
            Success = success,
            GoldStolen = goldStolen,
            ShieldConsumed = shieldConsumed,
            BalancesAfterJson = JsonSerializer.Serialize(balancesAfter),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.AttackLogs.Add(log);

        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync();
            existed = await _db.AttackLogs.FirstOrDefaultAsync(x => x.AttackerPlayerId == attackerPlayerId && x.RequestId == requestId);
            if (existed != null)
            {
                var balances = JsonSerializer.Deserialize<Dictionary<string, object?>>(existed.BalancesAfterJson) ?? new();
                return AttackResolveResult.Replay(new AttackResolveResponse(
                    Result: new AttackResult(existed.Success, existed.GoldStolen, existed.ShieldConsumed, 1f),
                    Balances: balances
                ));
            }
            throw;
        }

        return AttackResolveResult.Applied(new AttackResolveResponse(
            Result: new AttackResult(success, goldStolen, shieldConsumed, multiplier),
            Balances: balancesAfter
        ));
    }

    /// <summary>
    /// Bug #3 (r11): randomized target selection so the player doesn't always
    /// attack the same dummy. Strategy:
    ///   1. If there are other real players in the DB, pick one at random.
    ///   2. Otherwise, ensure a pool of 3-5 themed Vietnamese pirate bots exists
    ///      (created idempotently by stable GuestDeviceId keys), each with a
    ///      readable name and randomized gold/shields, then pick one at random.
    /// The attacker is always excluded.
    /// </summary>
    private async Task<Player?> PickTarget(Guid attackerPlayerId)
    {
        // 1. Any other real (non-bot) player? Pick a random one.
        var others = await _db.Players
            .Where(x => x.Id != attackerPlayerId && x.GuestDeviceId != "bot-target")
            .ToListAsync();
        if (others.Count > 0)
        {
            return others[Random.Shared.Next(others.Count)];
        }

        // 2. Ensure the themed bot pool exists (idempotent by stable device ids).
        var botPool = await EnsureBotPoolAsync();
        // Exclude the attacker just in case it happens to be a bot device id.
        var candidates = botPool.Where(b => b.Id != attackerPlayerId).ToList();
        if (candidates.Count == 0)
        {
            // Shouldn't happen, but keep a safe fallback.
            return botPool.FirstOrDefault();
        }
        return candidates[Random.Shared.Next(candidates.Count)];
    }

    /// <summary>
    /// Bug #3 (r11): idempotently creates a pool of 5 themed Vietnamese pirate
    /// bots with stable GuestDeviceId keys (so repeated calls don't duplicate
    /// them) and randomized gold/shield/island stats. Returns the full pool.
    /// </summary>
    private async Task<List<Player>> EnsureBotPoolAsync()
    {
        var botKeys = new[]
        {
            ("bot-râu-đen", "Thuyền trưởng Râu Đen"),
            ("bot-cưa-mù",  "Cướp biển Mù"),
            ("bot-bom",     "Thủy thủ Bom"),
            ("bot-vàng",    "Thuyền trưởng Vàng"),
            ("bot-đen",     "Cướp biển Đen")
        };

        var existing = await _db.Players
            .Where(x => x.GuestDeviceId.StartsWith("bot-"))
            .ToListAsync();

        // Index existing by device id for quick lookup.
        var byKey = existing.ToDictionary(p => p.GuestDeviceId);

        var created = false;
        foreach (var (key, name) in botKeys)
        {
            if (byKey.ContainsKey(key))
            {
                // Backfill the Name on older rows that predate the Name column.
                if (string.IsNullOrEmpty(byKey[key].Name))
                {
                    byKey[key].Name = name;
                    created = true;
                }
                continue;
            }

            var bot = new Player
            {
                GuestDeviceId = key,
                Name = name,
                Gold = Random.Shared.Next(3000, 12001),
                Spins = 0,
                ShieldCount = Random.Shared.Next(0, 4),
                CurrentIsland = Random.Shared.Next(1, 6),
                Level = Random.Shared.Next(1, 20),
                Xp = 0
            };
            _db.Players.Add(bot);
            byKey[key] = bot;
            created = true;
        }

        if (created)
        {
            await _db.SaveChangesAsync();
        }

        return byKey.Values.ToList();
    }
}

public sealed record AttackStartResponse(Guid AttackSessionId, AttackTarget Target);
public sealed record AttackTarget(Guid PlayerId, string Name, int CurrentIsland, int ShieldCount);

public sealed record AttackResolveResponse(AttackResult Result, Dictionary<string, object?> Balances);
public sealed record AttackResult(bool Success, long GoldStolen, bool ShieldConsumed, float ChestMultiplier = 1f);

public sealed class AttackStartResult
{
    private AttackStartResult() { }
    public bool Success { get; private init; }
    public bool WasReplayed { get; private init; }
    public AttackStartResponse? Response { get; private init; }
    public string? ErrorCode { get; private init; }
    public string? ErrorMessage { get; private init; }
    public object? ErrorDetails { get; private init; }
    public static AttackStartResult Applied(AttackStartResponse response) => new() { Success = true, Response = response };
    public static AttackStartResult Replay(AttackStartResponse response) => new() { Success = true, WasReplayed = true, Response = response };
    public static AttackStartResult Error(string code, string message, object? details) => new() { Success = false, ErrorCode = code, ErrorMessage = message, ErrorDetails = details };
}

public sealed class AttackResolveResult
{
    private AttackResolveResult() { }
    public bool Success { get; private init; }
    public bool WasReplayed { get; private init; }
    public AttackResolveResponse? Response { get; private init; }
    public string? ErrorCode { get; private init; }
    public string? ErrorMessage { get; private init; }
    public object? ErrorDetails { get; private init; }
    public static AttackResolveResult Applied(AttackResolveResponse response) => new() { Success = true, Response = response };
    public static AttackResolveResult Replay(AttackResolveResponse response) => new() { Success = true, WasReplayed = true, Response = response };
    public static AttackResolveResult Error(string code, string message, object? details) => new() { Success = false, ErrorCode = code, ErrorMessage = message, ErrorDetails = details };
}
