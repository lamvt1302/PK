namespace PK.Server.Domain;

public class PlayerIsland
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlayerId { get; set; }

    public int IslandId { get; set; } = 1;
    public int BuildingSlot { get; set; } = 1;  // 1..N
    public int BuildingLevel { get; set; } = 0; // 0..

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

