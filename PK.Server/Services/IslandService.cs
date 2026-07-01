using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PK.Server.Common;
using PK.Server.Data;
using PK.Server.Domain;

namespace PK.Server.Services;

public class IslandService
{
    private readonly PkDbContext _db;
    private readonly EconomyService _economy;

    public IslandService(PkDbContext db, EconomyService economy)
    {
        _db = db;
        _economy = economy;
    }

    public async Task<IslandStateResponse> GetIslandState(Guid playerId)
    {
        var player = await _db.Players.FirstAsync(x => x.Id == playerId);
        var islandId = player.CurrentIsland;

        // MVP: seed N slots nếu chưa có.
        const int slots = 5;
        var existing = await _db.PlayerIslands
            .Where(x => x.PlayerId == playerId && x.IslandId == islandId)
            .ToListAsync();

        if (existing.Count == 0)
        {
            for (var slot = 1; slot <= slots; slot++)
            {
                _db.PlayerIslands.Add(new PlayerIsland
                {
                    PlayerId = playerId,
                    IslandId = islandId,
                    BuildingSlot = slot,
                    BuildingLevel = slot == 1 ? 1 : 0
                });
            }

            await _db.SaveChangesAsync();
            existing = await _db.PlayerIslands
                .Where(x => x.PlayerId == playerId && x.IslandId == islandId)
                .ToListAsync();
        }

        return new IslandStateResponse(
            CurrentIsland: islandId,
            Buildings: existing
                .OrderBy(x => x.BuildingSlot)
                .Select(x => new BuildingState(x.BuildingSlot, x.BuildingLevel))
                .ToList()
        );
    }

    public async Task<IslandUpgradeResult> Upgrade(Guid playerId, Guid requestId, int slot)
    {
        // Validate slot range. The island has a fixed set of building slots (1..MaxSlot).
        // Out-of-range slots would otherwise cause a 500 when the building lookup fails.
        const int maxSlot = 5;
        if (slot < 1 || slot > maxSlot)
        {
            return IslandUpgradeResult.Error("INVALID_ARGUMENT", "Ô xây dựng không hợp lệ", new { slot, min = 1, max = maxSlot });
        }

        var existed = await _db.IslandUpgradeLogs.FirstOrDefaultAsync(x => x.PlayerId == playerId && x.RequestId == requestId);
        if (existed != null)
        {
            var balances = JsonSerializer.Deserialize<Dictionary<string, object?>>(existed.BalancesAfterJson) ?? new();
            return IslandUpgradeResult.Replay(new IslandUpgradeResponse(
                Upgraded: new BuildingState(existed.Slot, existed.AfterLevel),
                Balances: balances,
                Island: new { current_island = existed.IslandId }
            ));
        }

        await using var tx = await _db.Database.BeginTransactionAsync();

        // Re-check under tx
        existed = await _db.IslandUpgradeLogs.FirstOrDefaultAsync(x => x.PlayerId == playerId && x.RequestId == requestId);
        if (existed != null)
        {
            await tx.CommitAsync();
            var balances = JsonSerializer.Deserialize<Dictionary<string, object?>>(existed.BalancesAfterJson) ?? new();
            return IslandUpgradeResult.Replay(new IslandUpgradeResponse(
                Upgraded: new BuildingState(existed.Slot, existed.AfterLevel),
                Balances: balances,
                Island: new { current_island = existed.IslandId }
            ));
        }

        var player = await _db.Players.FirstAsync(x => x.Id == playerId);
        var islandId = player.CurrentIsland;

        var building = await _db.PlayerIslands.FirstOrDefaultAsync(x => x.PlayerId == playerId && x.IslandId == islandId && x.BuildingSlot == slot);
        if (building == null)
        {
            // seed state và retry
            await GetIslandState(playerId);
            building = await _db.PlayerIslands.FirstAsync(x => x.PlayerId == playerId && x.IslandId == islandId && x.BuildingSlot == slot);
        }

        var beforeLevel = building.BuildingLevel;
        var cost = CalculateUpgradeCost(islandId, beforeLevel);

        // pay cost via Economy (deterministic sub-request id)
        var costTxId = DeterministicGuid.From(requestId, "island-upgrade-cost");
        var pay = await _economy.ApplyTransaction(playerId, costTxId, "REMOVE_GOLD", cost, "ISLAND_UPGRADE_COST");
        if (!pay.Success)
        {
            await tx.RollbackAsync();
            return IslandUpgradeResult.Error(pay.ErrorCode ?? "INTERNAL", pay.ErrorMessage ?? "Có lỗi xíu, thử lại nha!", pay.ErrorDetails);
        }

        building.BuildingLevel = beforeLevel + 1;
        building.UpdatedAt = DateTimeOffset.UtcNow;

        var upgradeLog = new IslandUpgradeLog
        {
            PlayerId = playerId,
            RequestId = requestId,
            IslandId = islandId,
            Slot = slot,
            BeforeLevel = beforeLevel,
            AfterLevel = building.BuildingLevel,
            GoldCost = cost,
            BalancesAfterJson = "{}", // sẽ set trước khi commit (atomic)
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.IslandUpgradeLogs.Add(upgradeLog);

        // Lưu balances vào log trước khi commit để replay không bị rỗng nếu crash.
        // (player đã được Economy update trong cùng DbContext scope)
        var balancesAfter = new Dictionary<string, object?>
        {
            ["gold"] = player.Gold,
            ["spins"] = player.Spins,
            ["shield_count"] = player.ShieldCount
        };
        upgradeLog.BalancesAfterJson = JsonSerializer.Serialize(balancesAfter);

        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync();
            existed = await _db.IslandUpgradeLogs.FirstOrDefaultAsync(x => x.PlayerId == playerId && x.RequestId == requestId);
            if (existed != null)
            {
                var balances = JsonSerializer.Deserialize<Dictionary<string, object?>>(existed.BalancesAfterJson) ?? new();
                return IslandUpgradeResult.Replay(new IslandUpgradeResponse(
                    Upgraded: new BuildingState(existed.Slot, existed.AfterLevel),
                    Balances: balances,
                    Island: new { current_island = existed.IslandId }
                ));
            }

            throw;
        }

        return IslandUpgradeResult.Applied(new IslandUpgradeResponse(
            Upgraded: new BuildingState(slot, beforeLevel + 1),
            Balances: balancesAfter,
            Island: new { current_island = islandId }
        ));
    }

    private static long CalculateUpgradeCost(int islandId, int currentLevel)
    {
        // Placeholder curve: base * islandMultiplier * growth^level.
        // Sẽ được Economy Designer tune sau.
        var baseCost = 100.0;
        var islandMultiplier = 1.0 + (islandId - 1) * 0.15;
        var growth = 1.35;
        return (long)Math.Round(baseCost * islandMultiplier * Math.Pow(growth, currentLevel));
    }
}

public sealed record IslandStateResponse(int CurrentIsland, List<BuildingState> Buildings);
public sealed record BuildingState(int Slot, int Level);

public sealed record IslandUpgradeResponse(BuildingState Upgraded, Dictionary<string, object?> Balances, object Island);

public sealed class IslandUpgradeResult
{
    private IslandUpgradeResult() { }
    public bool Success { get; private init; }
    public bool WasReplayed { get; private init; }
    public IslandUpgradeResponse? Response { get; private init; }
    public string? ErrorCode { get; private init; }
    public string? ErrorMessage { get; private init; }
    public object? ErrorDetails { get; private init; }

    public static IslandUpgradeResult Applied(IslandUpgradeResponse response) => new() { Success = true, Response = response };
    public static IslandUpgradeResult Replay(IslandUpgradeResponse response) => new() { Success = true, WasReplayed = true, Response = response };
    public static IslandUpgradeResult Error(string code, string message, object? details) => new() { Success = false, ErrorCode = code, ErrorMessage = message, ErrorDetails = details };
}
