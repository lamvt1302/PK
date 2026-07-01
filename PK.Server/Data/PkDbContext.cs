using Microsoft.EntityFrameworkCore;
using PK.Server.Domain;

namespace PK.Server.Data;

public class PkDbContext : DbContext
{
    public PkDbContext(DbContextOptions<PkDbContext> options) : base(options) { }

    public DbSet<Player> Players => Set<Player>();
    public DbSet<EconomyLog> EconomyLogs => Set<EconomyLog>();
    public DbSet<SpinLog> SpinLogs => Set<SpinLog>();
    public DbSet<PlayerIsland> PlayerIslands => Set<PlayerIsland>();
    public DbSet<IslandUpgradeLog> IslandUpgradeLogs => Set<IslandUpgradeLog>();
    public DbSet<AttackSession> AttackSessions => Set<AttackSession>();
    public DbSet<AttackLog> AttackLogs => Set<AttackLog>();

    // Sprint-5: Event system (agent-11-event-service)
    public DbSet<GameEvent> GameEvents => Set<GameEvent>();
    public DbSet<PlayerEventClaim> PlayerEventClaims => Set<PlayerEventClaim>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Player>(b =>
        {
            b.HasIndex(x => x.GuestDeviceId).IsUnique();
        });

        modelBuilder.Entity<EconomyLog>(b =>
        {
            b.HasIndex(x => new { x.PlayerId, x.RequestId }).IsUnique();
            b.HasIndex(x => new { x.PlayerId, x.CreatedAt });
        });

        modelBuilder.Entity<SpinLog>(b =>
        {
            b.HasIndex(x => new { x.PlayerId, x.RequestId }).IsUnique();
            b.HasIndex(x => new { x.PlayerId, x.CreatedAt });
        });

        modelBuilder.Entity<PlayerIsland>(b =>
        {
            b.HasIndex(x => new { x.PlayerId, x.IslandId, x.BuildingSlot }).IsUnique();
        });

        modelBuilder.Entity<IslandUpgradeLog>(b =>
        {
            b.HasIndex(x => new { x.PlayerId, x.RequestId }).IsUnique();
        });

        modelBuilder.Entity<AttackSession>(b =>
        {
            b.HasCheckConstraint("chk_attack_not_self", "\"AttackerPlayerId\" <> \"TargetPlayerId\"");
            b.HasIndex(x => new { x.AttackerPlayerId, x.StartRequestId }).IsUnique();
        });

        modelBuilder.Entity<AttackLog>(b =>
        {
            b.HasIndex(x => new { x.AttackerPlayerId, x.RequestId }).IsUnique();
            b.HasIndex(x => new { x.AttackSessionId, x.CreatedAt });
            // Enforce "resolve only once per session" (giảm rủi ro double-transfer).
            b.HasIndex(x => x.AttackSessionId).IsUnique();
        });

        // Sprint-5: Event system
        modelBuilder.Entity<GameEvent>(b =>
        {
            b.HasIndex(x => x.EventCode).IsUnique();
            b.HasIndex(x => new { x.IsActive, x.StartAt, x.EndAt });
        });

        modelBuilder.Entity<PlayerEventClaim>(b =>
        {
            b.HasIndex(x => new { x.PlayerId, x.EventId }).IsUnique();
            b.HasIndex(x => x.PlayerId);
        });
    }
}
