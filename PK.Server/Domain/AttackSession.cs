namespace PK.Server.Domain;

public class AttackSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AttackerPlayerId { get; set; }
    public Guid TargetPlayerId { get; set; }

    public Guid StartRequestId { get; set; } // idempotency cho /attack/start

    public string Status { get; set; } = "STARTED"; // STARTED | RESOLVED

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

