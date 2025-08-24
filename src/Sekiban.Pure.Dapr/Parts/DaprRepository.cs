using Dapr;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Dapr.Actors;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Dapr.Parts;

/// <summary>
///     Repository implementation for Dapr actors, bridging between AggregateActor and AggregateEventHandlerActor.
///     This is the Dapr equivalent of Orleans' OrleansRepository.
/// </summary>
public class DaprRepository
{
    private readonly SekibanDomainTypes _domainTypes;
    private readonly IAggregateEventHandlerActor _eventHandlerActor;
    private readonly IEventTypes _eventTypes;
    private readonly PartitionKeys _partitionKeys;
    private readonly IAggregateProjector _projector;
    private Aggregate _currentAggregate;
    public DaprRepository(
        IAggregateEventHandlerActor eventHandlerActor,
        PartitionKeys partitionKeys,
        IAggregateProjector projector,
        IEventTypes eventTypes,
        Aggregate currentAggregate,
        SekibanDomainTypes domainTypes)
    {
        _eventHandlerActor = eventHandlerActor ?? throw new ArgumentNullException(nameof(eventHandlerActor));
        _partitionKeys = partitionKeys ?? throw new ArgumentNullException(nameof(partitionKeys));
        _projector = projector ?? throw new ArgumentNullException(nameof(projector));
        _eventTypes = eventTypes ?? throw new ArgumentNullException(nameof(eventTypes));
        _currentAggregate = currentAggregate ?? throw new ArgumentNullException(nameof(currentAggregate));
        _domainTypes = domainTypes;
    }

    public ResultBox<Aggregate> GetAggregate() => ResultBox<Aggregate>.FromValue(_currentAggregate);

    public async Task<ResultBox<List<IEvent>>> Save(string lastSortableUniqueId, List<IEvent> newEvents)
    {
        if (newEvents == null || newEvents.Count == 0)
        {
            return ResultBox<List<IEvent>>.FromValue(new List<IEvent>());
        }

        try
        {
            // Convert events to serializable documents
            var eventDocuments = new List<SerializableEventDocument>();
            foreach (var @event in newEvents)
            {
                var document = await SerializableEventDocument.CreateFromEventAsync(
                    @event,
                    _domainTypes.JsonSerializerOptions);
                eventDocuments.Add(document);
            }

            // Call the event handler actor to append events
            var response = await _eventHandlerActor.AppendEventsAsync(lastSortableUniqueId, eventDocuments);

            if (!response.IsSuccess)
            {
                return ResultBox<List<IEvent>>.FromException(new InvalidOperationException(response.ErrorMessage));
            }

            // Note: Unlike the previous implementation, we should NOT update _currentAggregate here
            // The caller (AggregateActor) will handle the aggregate update via GetProjectedAggregate
            Console.WriteLine(
                $"[DaprRepository.Save] Events saved: {newEvents.Count}, current version: {_currentAggregate.Version}");

            return ResultBox<List<IEvent>>.FromValue(newEvents);
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
            IReadOnlyList<SerializableEventDocument> eventDocuments;
            try
            {
                eventDocuments = await _eventHandlerActor.GetAllEventsAsync();
            }
            catch (DaprApiException ex) when (ex.Message.Contains("500"))
            {
                // Actor doesn't exist yet, return empty list
                eventDocuments = new List<SerializableEventDocument>();
            }

            // Convert documents back to events
            var events = new List<IEvent>();
            foreach (var document in eventDocuments)
            {
                var eventResult = await document.ToEventAsync(_domainTypes);
                if (eventResult.HasValue && eventResult.Value is not null)
                {
                    events.Add(eventResult.Value);
                }
            }

            // Start with empty aggregate
            var aggregate = Aggregate.EmptyFromPartitionKeys(_partitionKeys);

            // Project all events
            aggregate = aggregate.Project(events, _projector).UnwrapBox();

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
            Console.WriteLine(
                $"[GetProjectedAggregate] Current version: {_currentAggregate.Version}, projecting {projectedEvents.Count} events");

            // Project the events onto the current aggregate to get a new aggregate
            var aggregate = _currentAggregate.Project(projectedEvents, _projector).UnwrapBox();

            Console.WriteLine($"[GetProjectedAggregate] After projection - version: {aggregate.Version}");
            return ResultBox<Aggregate>.FromValue(aggregate);
        }
        catch (Exception ex)
        {
            return ResultBox<Aggregate>.FromException(ex);
        }
    }
}
