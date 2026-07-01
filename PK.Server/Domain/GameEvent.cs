using System.ComponentModel.DataAnnotations;

namespace PK.Server.Domain
{
    /// <summary>
    /// Event definition — config-driven, can be daily/weekly/seasonal.
    /// Agent-11-event-service owns this entity.
    /// </summary>
    public class GameEvent
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string EventCode { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// daily | weekly | seasonal | one_time
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string EventType { get; set; } = "daily";

        public DateTime StartAt { get; set; }
        public DateTime EndAt { get; set; }

        /// <summary>
        /// JSON config: rewards, missions, conditions.
        /// </summary>
        public string ConfigJson { get; set; } = "{}";

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Per-player event participation + claim tracking.
    /// </summary>
    public class PlayerEventClaim
    {
        public int Id { get; set; }
        public Guid PlayerId { get; set; }
        public int EventId { get; set; }

        /// <summary>
        /// claimed | completed | expired
        /// </summary>
        [MaxLength(20)]
        public string Status { get; set; } = "claimed";

        /// <summary>
        /// JSON: which missions/rewards were claimed.
        /// </summary>
        public string ClaimDataJson { get; set; } = "{}";

        public DateTime ClaimedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public Player? Player { get; set; }
        public GameEvent? Event { get; set; }
    }
}