using Microsoft.Extensions.Logging;
using Orleans.Streams;
using Sekiban.Pure.Events;

namespace AspireEventSample.ApiService.Aggregates.ReadModel;

/// <summary>
/// Orleans stream event source adapter
/// </summary>
public class OrleansStreamEventSourceAdapter
{
    private readonly EventProcessor _eventProcessor;
    private readonly ILogger<OrleansStreamEventSourceAdapter> _logger;
    
    public OrleansStreamEventSourceAdapter(
        EventProcessor eventProcessor,
        ILogger<OrleansStreamEventSourceAdapter> logger)
    {
        _eventProcessor = eventProcessor;
        _logger = logger;
    }
    
    /// <summary>
    /// Process stream event
    /// </summary>
    public Task ProcessStreamEventAsync(IEvent @event, StreamSequenceToken? token)
    {
        _logger.LogDebug("Processing stream event {EventType} with ID {EventId}",
            @event.GetPayload().GetType().Name, @event.PartitionKeys.AggregateId);
            
        return _eventProcessor.ProcessEventAsync(@event);
    }
}
