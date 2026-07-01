namespace PK.Server.Domain;

public class AttackLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AttackSessionId { get; set; }
    public Guid AttackerPlayerId { get; set; }
    public Guid TargetPlayerId { get; set; }

    public Guid RequestId { get; set; } // idempotency key cho /attack/resolve

    public string ClientInputJson { get; set; } = "{}";

    public bool Success { get; set; }
    public long GoldStolen { get; set; }
    public bool ShieldConsumed { get; set; }

    public string BalancesAfterJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

