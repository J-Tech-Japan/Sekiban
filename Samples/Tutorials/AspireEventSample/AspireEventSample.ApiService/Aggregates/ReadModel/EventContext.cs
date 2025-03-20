using Sekiban.Pure.Events;

namespace AspireEventSample.ApiService.Aggregates.ReadModel;

/// <summary>
/// Event context
/// </summary>
public class EventContext
{
    /// <summary>
    /// Event
    /// </summary>
    public IEvent Event { get; }
    
    /// <summary>
    /// Root partition key
    /// </summary>
    public string RootPartitionKey => Event.PartitionKeys.RootPartitionKey;
    
    /// <summary>
    /// Aggregate group
    /// </summary>
    public string AggregateGroup => Event.PartitionKeys.Group;
    
    /// <summary>
    /// Target ID
    /// </summary>
    public Guid TargetId => Event.PartitionKeys.AggregateId;
    
    /// <summary>
    /// Sortable unique ID
    /// </summary>
    public string SortableUniqueId => Event.SortableUniqueId;
    
    public EventContext(IEvent @event)
    {
        Event = @event;
    }
}
