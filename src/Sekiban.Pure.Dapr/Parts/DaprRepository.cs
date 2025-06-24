using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Repositories;
using Sekiban.Pure.Dapr.Actors;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Command;
using Sekiban.Pure.Command.Handlers;

namespace Sekiban.Pure.Dapr.Parts;

/// <summary>
/// Repository implementation for Dapr actors, bridging between AggregateActor and AggregateEventHandlerActor.
/// This is the Dapr equivalent of Orleans' OrleansRepository.
/// </summary>
public class DaprRepository
{
    private readonly IAggregateEventHandlerActor _eventHandlerActor;
    private readonly PartitionKeys _partitionKeys;
    private readonly IAggregateProjector _projector;
    private readonly IEventTypes _eventTypes;
    private Aggregate _currentAggregate;

    public DaprRepository(
        IAggregateEventHandlerActor eventHandlerActor,
        PartitionKeys partitionKeys,
        IAggregateProjector projector,
        IEventTypes eventTypes,
        Aggregate currentAggregate)
    {
        _eventHandlerActor = eventHandlerActor ?? throw new ArgumentNullException(nameof(eventHandlerActor));
        _partitionKeys = partitionKeys ?? throw new ArgumentNullException(nameof(partitionKeys));
        _projector = projector ?? throw new ArgumentNullException(nameof(projector));
        _eventTypes = eventTypes ?? throw new ArgumentNullException(nameof(eventTypes));
        _currentAggregate = currentAggregate ?? throw new ArgumentNullException(nameof(currentAggregate));
    }

    public ResultBox<Aggregate> GetAggregate()
    {
        return ResultBox<Aggregate>.FromValue(_currentAggregate);
    }

    public async Task<ResultBox<List<IEvent>>> Save(
        string lastSortableUniqueId,
        List<IEvent> newEvents)
    {
        if (newEvents == null || newEvents.Count == 0)
        {
            return ResultBox<List<IEvent>>.FromValue(new List<IEvent>());
        }

        try
        {
            // Append events to the event handler actor
            var storedEvents = await _eventHandlerActor.AppendEventsAsync(
                lastSortableUniqueId,
                newEvents);

            // Update current aggregate with new events
            _currentAggregate = _currentAggregate.Project(storedEvents.ToList(), _projector).UnwrapBox();

            return ResultBox<List<IEvent>>.FromValue(storedEvents.ToList());
        }
        catch (Exception ex)
        {
            return ResultBox<List<IEvent>>.FromException(ex);
        }
    }

    public async Task<ResultBox<Aggregate>> Load()
    {
        try
        {
            // Get all events from the event handler
            var events = await _eventHandlerActor.GetAllEventsAsync();
            
            // Start with empty aggregate
            var aggregate = Aggregate.EmptyFromPartitionKeys(_partitionKeys);
            
            // Project all events
            aggregate = aggregate.Project(events.ToList(), _projector).UnwrapBox();
            
            // Update current aggregate
            _currentAggregate = aggregate;
            
            return ResultBox<Aggregate>.FromValue(aggregate);
        }
        catch (Exception ex)
        {
            return ResultBox<Aggregate>.FromException(ex);
        }
    }

    public ResultBox<Aggregate> GetProjectedAggregate(List<IEvent> projectedEvents)
    {
        try
        {
            var aggregate = _currentAggregate.Project(projectedEvents, _projector).UnwrapBox();
            return ResultBox<Aggregate>.FromValue(aggregate);
        }
        catch (Exception ex)
        {
            return ResultBox<Aggregate>.FromException(ex);
        }
    }
}