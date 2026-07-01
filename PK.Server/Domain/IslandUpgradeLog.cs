namespace PK.Server.Domain;

public class IslandUpgradeLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlayerId { get; set; }
    public Guid RequestId { get; set; } // idempotency key cho /island/upgrade

    public int IslandId { get; set; }
    public int Slot { get; set; }
    public int BeforeLevel { get; set; }
    public int AfterLevel { get; set; }

    public long GoldCost { get; set; }

    public string BalancesAfterJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
