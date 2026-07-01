namespace PK.Server.Domain;

public class SpinLog
{
    public Guid Id { get; set; } = Guid.NewGuid(); // spin_id
    public Guid PlayerId { get; set; }
    public Guid RequestId { get; set; } // idempotency key cho /spin request

    public string RewardType { get; set; } = string.Empty;
    public string RewardPayloadJson { get; set; } = "{}";
    public string BalancesAfterJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

