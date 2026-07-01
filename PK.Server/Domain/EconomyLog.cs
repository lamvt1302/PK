namespace PK.Server.Domain;

public class EconomyLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlayerId { get; set; }

    // Idempotency key cho transaction level.
    public Guid RequestId { get; set; }

    public string TransactionType { get; set; } = string.Empty; // ADD_GOLD, REMOVE_GOLD, ADD_SPIN, REMOVE_SPIN...
    public string Currency { get; set; } = string.Empty;        // gold, spins
    public long Amount { get; set; }

    public long BeforeValue { get; set; }
    public long AfterValue { get; set; }

    public string Reason { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

