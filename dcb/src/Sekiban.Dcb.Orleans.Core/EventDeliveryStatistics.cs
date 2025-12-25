namespace Sekiban.Dcb.Orleans;

/// <summary>
/// Statistics about event delivery for debugging duplicate/missing events
/// </summary>
[GenerateSerializer]
public class EventDeliveryStatistics
{
    /// <summary>
    /// Total number of unique events (distinct by GUID)
    /// </summary>
    [Id(0)]
    public int TotalUniqueEvents { get; set; }
    
    /// <summary>
    /// Total number of deliveries (including duplicates)
    /// </summary>
    [Id(1)]
    public long TotalDeliveries { get; set; }
    
    /// <summary>
    /// Number of duplicate deliveries
    /// </summary>
    [Id(2)]
    public long DuplicateDeliveries { get; set; }
    
    /// <summary>
    /// Number of events that were delivered more than once
    /// </summary>
    [Id(3)]
    public int EventsWithMultipleDeliveries { get; set; }
    
    /// <summary>
    /// Maximum number of times a single event was delivered
    /// </summary>
    [Id(4)]
    public int MaxDeliveryCount { get; set; }
    
    /// <summary>
    /// Average delivery count per event
    /// </summary>
    [Id(5)]
    public double AverageDeliveryCount { get; set; }

    /// <summary>
    /// Total unique events received via Orleans stream
    /// </summary>
    [Id(6)]
    public int StreamUniqueEvents { get; set; }

    /// <summary>
    /// Total deliveries received via Orleans stream
    /// </summary>
    [Id(7)]
    public long StreamDeliveries { get; set; }

    /// <summary>
    /// Total unique events received via EventStore catch-up
    /// </summary>
    [Id(8)]
    public int CatchUpUniqueEvents { get; set; }

    /// <summary>
    /// Total deliveries received via EventStore catch-up
    /// </summary>
    [Id(9)]
    public long CatchUpDeliveries { get; set; }

    /// <summary>
    /// Optional message (e.g., when statistics are not being recorded).
    /// </summary>
    [Id(10)]
    public string? Message { get; set; }
}
