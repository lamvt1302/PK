using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PK.Server.Common;
using PK.Server.Data;
using PK.Server.Domain;

namespace PK.Server.Services;

public class SpinService
{
    private readonly PkDbContext _db;
    private readonly EconomyService _economy;

    public SpinService(PkDbContext db, EconomyService economy)
    {
        _db = db;
        _economy = economy;
    }

    public async Task<SpinResponseResult> Spin(Guid playerId, Guid requestId)
    {
        var existed = await _db.SpinLogs.FirstOrDefaultAsync(x => x.PlayerId == playerId && x.RequestId == requestId);
        if (existed != null)
        {
            return SpinResponseResult.Replay(ParseResponse(existed));
        }

        await using var tx = await _db.Database.BeginTransactionAsync();

        // Re-check under transaction
        existed = await _db.SpinLogs.FirstOrDefaultAsync(x => x.PlayerId == playerId && x.RequestId == requestId);
        if (existed != null)
        {
            await tx.CommitAsync();
            return SpinResponseResult.Replay(ParseResponse(existed));
        }

        // Consume 1 spin (idempotent sub-transaction)
        var consumeTxId = DeterministicGuid.From(requestId, "spin-consume");
        var consume = await _economy.ApplyTransaction(playerId, consumeTxId, "REMOVE_SPIN", 1, "SPIN_CONSUME");
        if (!consume.Success)
        {
            await tx.RollbackAsync();
            return SpinResponseResult.Error(consume.ErrorCode ?? "INTERNAL", consume.ErrorMessage ?? "Có lỗi xíu, thử lại nha!", consume.ErrorDetails);
        }

        // Load player state after consume
        var player = await _db.Players.FirstAsync(x => x.Id == playerId);

        // RNG server-side
        var (rewardType, rewardPayload) = RollReward(player);

        // For attack/raid rewards, create an attack session up-front so the spin
        // result carries an attack_session_id the client can use to resolve the attack.
        if (rewardType is "attack" or "raid")
        {
            var targetPlayer = await PickAttackTarget(playerId);
            if (targetPlayer != null)
            {
                var session = new AttackSession
                {
                    AttackerPlayerId = playerId,
                    TargetPlayerId = targetPlayer.Id,
                    StartRequestId = DeterministicGuid.From(requestId, "spin-attack-start"),
                    Status = "STARTED",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _db.AttackSessions.Add(session);
                await _db.SaveChangesAsync();
                rewardPayload["attack_session_id"] = session.Id.ToString();
            }
        }

        // Apply reward (idempotent sub-transaction(s) or direct fields)
        var balances = new Dictionary<string, object?>
        {
            ["gold"] = player.Gold,
            ["spins"] = player.Spins,
            ["shield_count"] = player.ShieldCount
        };

        if (rewardType is "gold_small" or "gold_big")
        {
            var gold = (long)rewardPayload["gold"]!;
            var rewardTxId = DeterministicGuid.From(requestId, "spin-reward-gold");
            var add = await _economy.ApplyTransaction(playerId, rewardTxId, "ADD_GOLD", gold, "SPIN_REWARD");
            if (!add.Success)
            {
                await tx.RollbackAsync();
                return SpinResponseResult.Error(add.ErrorCode ?? "INTERNAL", add.ErrorMessage ?? "Có lỗi xíu, thử lại nha!", add.ErrorDetails);
            }

            player = await _db.Players.FirstAsync(x => x.Id == playerId);
        }
        else if (rewardType == "spin_bonus")
        {
            var spins = (long)rewardPayload["spins"]!;
            var txId = DeterministicGuid.From(requestId, "spin-reward-spins");
            var add = await _economy.ApplyTransaction(playerId, txId, "ADD_SPIN", spins, "SPIN_REWARD");
            if (!add.Success)
            {
                await tx.RollbackAsync();
                return SpinResponseResult.Error(add.ErrorCode ?? "INTERNAL", add.ErrorMessage ?? "Có lỗi xíu, thử lại nha!", add.ErrorDetails);
            }

            player = await _db.Players.FirstAsync(x => x.Id == playerId);
        }
        else if (rewardType == "shield")
        {
            // Shield count cap (placeholder theo economy v1).
            player.ShieldCount = Math.Min(3, player.ShieldCount + 1);
            player.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync();
        }
        else if (rewardType is "attack" or "raid")
        {
            // MVP scaffold: chỉ trả payload để UI hiển thị.
            // Token/inventory model sẽ làm chặt hơn khi triển khai đầy đủ.
        }

        balances["gold"] = player.Gold;
        balances["spins"] = player.Spins;
        balances["shield_count"] = player.ShieldCount;

        var response = new SpinResponse(
            SpinId: Guid.NewGuid(),
            Result: new SpinResult(rewardType, rewardPayload),
            Balances: balances
        );

        var log = new SpinLog
        {
            Id = response.SpinId,
            PlayerId = playerId,
            RequestId = requestId,
            RewardType = rewardType,
            RewardPayloadJson = JsonSerializer.Serialize(rewardPayload),
            BalancesAfterJson = JsonSerializer.Serialize(balances),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.SpinLogs.Add(log);

        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync();
            existed = await _db.SpinLogs.FirstOrDefaultAsync(x => x.PlayerId == playerId && x.RequestId == requestId);
            if (existed != null) return SpinResponseResult.Replay(ParseResponse(existed));
            throw;
        }

        return SpinResponseResult.Applied(response);
    }

    /// <summary>
    /// Bug #2 (r4): grants the daily free-spin reward (5 spins). Idempotent per
    /// UTC day per player: the underlying economy transaction uses a deterministic
    /// request id derived from the player id + the UTC date, so claiming twice on
    /// the same day replays the same transaction instead of double-granting. The
    /// next day (new UTC date) yields a new id and grants another 5 spins.
    /// </summary>
    public async Task<DailyRewardResult> ClaimDailySpinReward(Guid playerId)
    {
        var today = DateTimeOffset.UtcNow.Date;
        // Deterministic id per (player, UTC day) so the economy transaction is
        // naturally idempotent for the whole day.
        var rewardTxId = DeterministicGuid.From(playerId, $"daily-spin-{today:yyyy-MM-dd}");

        const int rewardSpins = 5;

        await using var tx = await _db.Database.BeginTransactionAsync();

        var add = await _economy.ApplyTransaction(playerId, rewardTxId, "ADD_SPIN", rewardSpins, "DAILY_FREE_SPINS");
        if (!add.Success && !add.WasReplayed)
        {
            await tx.RollbackAsync();
            return DailyRewardResult.Error(add.ErrorCode ?? "INTERNAL", add.ErrorMessage ?? "Có lỗi xíu, thử lại nha!", add.ErrorDetails);
        }

        await tx.CommitAsync();

        var player = await _db.Players.FirstAsync(x => x.Id == playerId);
        var balances = new Dictionary<string, object?>
        {
            ["gold"] = player.Gold,
            ["spins"] = player.Spins,
            ["shield_count"] = player.ShieldCount
        };

        return DailyRewardResult.Applied(new DailyRewardResponse(
            SpinsGranted: rewardSpins,
            WasReplayed: add.WasReplayed,
            Balances: balances
        ));
    }

    private static SpinResponse ParseResponse(SpinLog log)
    {
        // Bug #1 (r10): deserialize with JsonNode-backed values then convert to
        // plain CLR types (long/string) so the controller's (long?)gold cast works
        // on the idempotency replay path. Previously Deserialize<Dictionary<string,
        // object?>> produced JsonElement values, and (long?)jsonElement threw
        // InvalidCastException -> ErrorHandlingMiddleware caught it and returned
        // 500 INTERNAL instead of replaying the original 200 response.
        var payload = NormalizeJsonValues(JsonSerializer.Deserialize<Dictionary<string, object?>>(log.RewardPayloadJson) ?? new());
        var balances = NormalizeJsonValues(JsonSerializer.Deserialize<Dictionary<string, object?>>(log.BalancesAfterJson) ?? new());
        return new SpinResponse(
            SpinId: log.Id,
            Result: new SpinResult(log.RewardType, payload),
            Balances: balances
        );
    }

    /// <summary>
    /// Bug #1 (r10): converts JsonElement values (produced when deserializing to
    /// Dictionary&lt;string, object?&gt;) into plain CLR types so downstream casts
    /// like (long?)value succeed on the idempotency replay path. Numbers become
    /// long, strings stay strings, everything else is left as-is.
    /// </summary>
    private static Dictionary<string, object?> NormalizeJsonValues(Dictionary<string, object?> dict)
    {
        var result = new Dictionary<string, object?>(dict.Count);
        foreach (var kv in dict)
        {
            result[kv.Key] = kv.Value switch
            {
                JsonElement je => NormalizeJsonElement(je),
                _ => kv.Value
            };
        }
        return result;
    }

    private static object? NormalizeJsonElement(JsonElement je)
    {
        return je.ValueKind switch
        {
            JsonValueKind.Number => je.TryGetInt64(out var l) ? l : (object?)je.GetDouble(),
            JsonValueKind.String => je.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => je.GetRawText()
        };
    }

    /// <summary>
    /// Picks a target player for an attack/raid spin reward (MVP heuristic:
    /// any other player; falls back to the shared bot target).
    /// </summary>
    private async Task<Player?> PickAttackTarget(Guid attackerPlayerId)
    {
        var target = await _db.Players.FirstOrDefaultAsync(x => x.Id != attackerPlayerId);
        if (target != null) return target;

        var bot = await _db.Players.FirstOrDefaultAsync(x => x.GuestDeviceId == "bot-target");
        if (bot != null) return bot;

        bot = new Player
        {
            GuestDeviceId = "bot-target",
            Gold = 500,
            Spins = 0,
            ShieldCount = 1,
            CurrentIsland = 1,
            Level = 1,
            Xp = 0
        };
        _db.Players.Add(bot);
        await _db.SaveChangesAsync();
        return bot;
    }

    private static (string rewardType, Dictionary<string, object?> payload) RollReward(Player player)
    {
        // Placeholder weights theo `pk-docs/04_economy-design-v1.md`.
        // Bug #2 (r7): tăng weight của spin_bonus từ 5 -> 10 để xác suất rơi vào
        // khoảng 1/10 (trước đây 1/20 khiến 22 lượt liên tiếp không trúng là khả
        // dĩ nhưng dễ khiến người chơi tưởng bug). Tổng weight giờ = 105.
        var entries = new (string type, int weight)[]
        {
            ("gold_small", 40),
            ("gold_big", 20),
            ("attack", 15),
            ("raid", 10),
            ("shield", 10),
            ("spin_bonus", 10)
        };

        var total = entries.Sum(x => x.weight);
        var r = Random.Shared.Next(1, total + 1);
        var acc = 0;
        string picked = entries[0].type;
        foreach (var e in entries)
        {
            acc += e.weight;
            if (r <= acc)
            {
                picked = e.type;
                break;
            }
        }

        // Payload conventions (placeholder).
        return picked switch
        {
            "gold_small" => ("gold_small", new Dictionary<string, object?> { ["gold"] = (long)Random.Shared.Next(50, 121) }),
            "gold_big" => ("gold_big", new Dictionary<string, object?> { ["gold"] = (long)Random.Shared.Next(200, 601) }),
            "attack" => ("attack", new Dictionary<string, object?> { ["attack_token"] = 1L }),
            "raid" => ("raid", new Dictionary<string, object?> { ["raid_token"] = 1L }),
            "shield" => ("shield", new Dictionary<string, object?> { ["shield"] = 1L }),
            "spin_bonus" => ("spin_bonus", new Dictionary<string, object?> { ["spins"] = (long)Random.Shared.Next(5, 11) }),
            _ => ("gold_small", new Dictionary<string, object?> { ["gold"] = 80L })
        };
    }
}

public sealed record SpinResponse(Guid SpinId, SpinResult Result, Dictionary<string, object?> Balances);
public sealed record SpinResult(string Type, Dictionary<string, object?> Payload);

public sealed class SpinResponseResult
{
    private SpinResponseResult() { }

    public bool Success { get; private init; }
    public bool WasReplayed { get; private init; }
    public SpinResponse? Response { get; private init; }

    public string? ErrorCode { get; private init; }
    public string? ErrorMessage { get; private init; }
    public object? ErrorDetails { get; private init; }

    public static SpinResponseResult Applied(SpinResponse response) => new() { Success = true, Response = response };
    public static SpinResponseResult Replay(SpinResponse response) => new() { Success = true, WasReplayed = true, Response = response };
    public static SpinResponseResult Error(string code, string message, object? details) => new() { Success = false, ErrorCode = code, ErrorMessage = message, ErrorDetails = details };
}

// Bug #2 (r4): daily free-spin reward result types.
public sealed record DailyRewardResponse(int SpinsGranted, bool WasReplayed, Dictionary<string, object?> Balances);

public sealed class DailyRewardResult
{
    private DailyRewardResult() { }

    public bool Success { get; private init; }
    public DailyRewardResponse? Response { get; private init; }
    public string? ErrorCode { get; private init; }
    public string? ErrorMessage { get; private init; }
    public object? ErrorDetails { get; private init; }

    public static DailyRewardResult Applied(DailyRewardResponse response) => new() { Success = true, Response = response };
    public static DailyRewardResult Error(string code, string message, object? details) => new() { Success = false, ErrorCode = code, ErrorMessage = message, ErrorDetails = details };
}
