namespace PK.Server.Domain;

public class Player
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Idempotency theo device_id (guest login).
    public string GuestDeviceId { get; set; } = string.Empty;

    // Bug #3 (r11): human-readable name for attack targets / leaderboards. Bots
    // get themed Vietnamese pirate names; real players default to a friendly
    // placeholder until they set one. Non-null so DB queries don't blow up on
    // missing column data before the migration backfill runs.
    public string Name { get; set; } = string.Empty;

    public int Level { get; set; } = 1;
    public long Xp { get; set; } = 0;

    public long Gold { get; set; } = 1000;
    public int Spins { get; set; } = 10;
    public int ShieldCount { get; set; } = 0;
    public int CurrentIsland { get; set; } = 1;

    public bool IsBanned { get; set; } = false;
    public string? BanReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

