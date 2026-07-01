using Microsoft.EntityFrameworkCore;
using PK.Server.Common;
using PK.Server.Data;
using PK.Server.Domain;

namespace PK.Server.Services;

public class EconomyService
{
    private readonly PkDbContext _db;

    public EconomyService(PkDbContext db)
    {
        _db = db;
    }

    public async Task<EconomyResult> ApplyTransaction(
        Guid playerId,
        Guid requestId,
        string type,
        long amount,
        string reason)
    {
        if (amount <= 0) throw new ArgumentException("Số lượng phải lớn hơn 0");

        var existed = await _db.EconomyLogs.FirstOrDefaultAsync(x => x.PlayerId == playerId && x.RequestId == requestId);
        if (existed != null)
        {
            // Payload conflict check (tối thiểu).
            if (!string.Equals(existed.TransactionType, type, StringComparison.OrdinalIgnoreCase)
                || existed.Amount != amount
                || !string.Equals(existed.Reason, reason, StringComparison.OrdinalIgnoreCase))
            {
                return EconomyResult.Conflict(existed);
            }

            return EconomyResult.Replay(existed);
        }

        var createdTx = false;
        var tx = _db.Database.CurrentTransaction;
        if (tx == null)
        {
            tx = await _db.Database.BeginTransactionAsync();
            createdTx = true;
        }

        var player = await _db.Players.FirstOrDefaultAsync(x => x.Id == playerId);
        if (player == null) return EconomyResult.PlayerNotFound();

        // Re-check under transaction
        existed = await _db.EconomyLogs.FirstOrDefaultAsync(x => x.PlayerId == playerId && x.RequestId == requestId);
        if (existed != null)
        {
            await tx.CommitAsync();
            return EconomyResult.Replay(existed);
        }

        string currency;
        long before;
        long after;
        try
        {
            (currency, before, after) = ApplyToPlayer(player, type, amount);
        }
        catch (EconomyRuleException ex)
        {
            if (createdTx) await tx.RollbackAsync();
            return EconomyResult.RuleError(ex);
        }

        var log = new EconomyLog
        {
            PlayerId = playerId,
            RequestId = requestId,
            TransactionType = type,
            Currency = currency,
            Amount = amount,
            BeforeValue = before,
            AfterValue = after,
            Reason = reason,
            CreatedAt = DateTimeOffset.UtcNow
        };

        player.UpdatedAt = DateTimeOffset.UtcNow;
        _db.EconomyLogs.Add(log);

        try
        {
            await _db.SaveChangesAsync();
            if (createdTx) await tx.CommitAsync();
        }
        catch (DbUpdateException)
        {
            if (createdTx) await tx.RollbackAsync();
            // Unique constraint triggered -> treat as replay
            existed = await _db.EconomyLogs.FirstOrDefaultAsync(x => x.PlayerId == playerId && x.RequestId == requestId);
            if (existed != null) return EconomyResult.Replay(existed);
            throw;
        }

        return EconomyResult.Applied(log);
    }

    public async Task<(bool ok, string? errorCode, string? errorMessage)> TransferGold(
        Guid requestId,
        Guid fromPlayerId,
        Guid toPlayerId,
        long amount,
        string reason)
    {
        if (fromPlayerId == toPlayerId) return (false, "INVALID_ARGUMENT", "Không thể chuyển cho chính mình");
        if (amount <= 0) return (false, "INVALID_ARGUMENT", "Số lượng phải lớn hơn 0");

        // Deterministic sub-ids per leg.
        var debitId = DeterministicGuid.From(requestId, $"debit:{fromPlayerId:D}");
        var creditId = DeterministicGuid.From(requestId, $"credit:{toPlayerId:D}");

        var createdTx = false;
        var tx = _db.Database.CurrentTransaction;
        if (tx == null)
        {
            tx = await _db.Database.BeginTransactionAsync();
            createdTx = true;
        }

        var from = await _db.Players.FirstOrDefaultAsync(x => x.Id == fromPlayerId);
        var to = await _db.Players.FirstOrDefaultAsync(x => x.Id == toPlayerId);
        if (from == null || to == null) return (false, "PLAYER_NOT_FOUND", "Không tìm thấy người chơi");

        // Debit (idempotent)
        var debit = await ApplyTransaction(fromPlayerId, debitId, "REMOVE_GOLD", amount, reason);
        if (!debit.Success)
        {
            if (createdTx) await tx.RollbackAsync();
            return (false, debit.ErrorCode, debit.ErrorMessage);
        }

        // Credit (idempotent)
        var credit = await ApplyTransaction(toPlayerId, creditId, "ADD_GOLD", amount, reason);
        if (!credit.Success)
        {
            if (createdTx) await tx.RollbackAsync();
            return (false, credit.ErrorCode, credit.ErrorMessage);
        }

        if (createdTx) await tx.CommitAsync();
        return (true, null, null);
    }

    private static (string currency, long before, long after) ApplyToPlayer(Player player, string type, long amount)
    {
        switch (type.ToUpperInvariant())
        {
            case "ADD_GOLD":
                {
                    var before = player.Gold;
                    var after = before + amount;
                    player.Gold = after;
                    return ("gold", before, after);
                }
            case "REMOVE_GOLD":
                {
                    var before = player.Gold;
                    if (before < amount) throw new EconomyRuleException("INSUFFICIENT_FUNDS", "Không đủ vàng!", new { currency = "gold", required = amount, current = before });
                    var after = before - amount;
                    player.Gold = after;
                    return ("gold", before, after);
                }
            case "ADD_SPIN":
                {
                    var before = player.Spins;
                    var after = before + (int)amount;
                    player.Spins = after;
                    return ("spins", before, after);
                }
            case "REMOVE_SPIN":
                {
                    var before = player.Spins;
                    if (before < amount) throw new EconomyRuleException("NO_SPINS", "Hết lượt quay!", new { required = amount, current = before });
                    var after = before - (int)amount;
                    player.Spins = after;
                    return ("spins", before, after);
                }
            default:
                throw new EconomyRuleException("INVALID_ARGUMENT", "Loại giao dịch không hợp lệ", new { type });
        }
    }
}

public sealed class EconomyRuleException : Exception
{
    public string Code { get; }
    public string MessageSafe { get; }
    public object Details { get; }

    public EconomyRuleException(string code, string message, object details) : base(message)
    {
        Code = code;
        MessageSafe = message;
        Details = details;
    }
}

public sealed class EconomyResult
{
    private EconomyResult() { }

    public bool Success { get; private init; }
    public bool WasApplied { get; private init; }
    public bool WasReplayed { get; private init; }
    public bool WasConflict { get; private init; }

    public EconomyLog? Log { get; private init; }

    public string? ErrorCode { get; private init; }
    public string? ErrorMessage { get; private init; }
    public object? ErrorDetails { get; private init; }

    public static EconomyResult Applied(EconomyLog log) => new() { Success = true, WasApplied = true, Log = log };
    public static EconomyResult Replay(EconomyLog log) => new() { Success = true, WasReplayed = true, Log = log };
    public static EconomyResult Conflict(EconomyLog log) => new() { Success = false, WasConflict = true, ErrorCode = "IDEMPOTENCY_KEY_CONFLICT", ErrorMessage = "Yêu cầu này đã được xử lý rồi", Log = log };
    public static EconomyResult PlayerNotFound() => new() { Success = false, ErrorCode = "PLAYER_NOT_FOUND", ErrorMessage = "Không tìm thấy người chơi" };

    public static EconomyResult RuleError(EconomyRuleException ex) => new()
    {
        Success = false,
        ErrorCode = ex.Code,
        ErrorMessage = ex.MessageSafe,
        ErrorDetails = ex.Details
    };
}
