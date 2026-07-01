using Microsoft.EntityFrameworkCore;
using PK.Server.Data;
using PK.Server.Domain;

namespace PK.Server.Services;

public class PlayerService
{
    private readonly PkDbContext _db;

    public PlayerService(PkDbContext db)
    {
        _db = db;
    }

    public async Task<Player> GetOrCreateGuestPlayer(string deviceId)
    {
        // Fast path
        var existing = await _db.Players.FirstOrDefaultAsync(x => x.GuestDeviceId == deviceId);
        if (existing != null) return existing;

        // Create path: handle race by catching unique constraint violation.
        var player = new Player
        {
            GuestDeviceId = deviceId,
            Gold = 1000,
            Spins = 10,
            ShieldCount = 0,
            CurrentIsland = 1,
            Level = 1,
            Xp = 0
        };

        _db.Players.Add(player);
        try
        {
            await _db.SaveChangesAsync();
            return player;
        }
        catch (DbUpdateException)
        {
            // Another request created it first (race condition).
            var retry = await _db.Players.FirstOrDefaultAsync(x => x.GuestDeviceId == deviceId);
            if (retry != null) return retry;
            throw; // Re-throw nếu thật sự không tạo được
        }
    }

    public async Task<Player?> GetPlayer(Guid playerId)
    {
        return await _db.Players.FirstOrDefaultAsync(x => x.Id == playerId);
    }
}

