using Microsoft.Extensions.Logging;
using Sekiban.Pure.Events;
namespace Sekiban.Pure.ReadModel;

/// <summary>
///     Event processor for read models
/// </summary>
public class EventProcessor
{
    private readonly IEventContextProvider _eventContextProvider;
    private readonly IEnumerable<IReadModelHandler> _handlers;
    private readonly ILogger<EventProcessor> _logger;

    public EventProcessor(
        IEnumerable<IReadModelHandler> handlers,
        IEventContextProvider eventContextProvider,
        ILogger<EventProcessor> logger)
    {
        _handlers = handlers;
        _eventContextProvider = eventContextProvider;
        _logger = logger;
    }

    /// <summary>
    ///     Process event
    /// </summary>
    public async Task ProcessEventAsync(IEvent @event)
    {
        try
        {
            // Set event context
            _eventContextProvider.SetCurrentEventContext(@event);

            // Process event with all handlers
            var tasks = _handlers.Select(h => ProcessEventWithHandlerAsync(h, @event));
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing event {EventType} with ID {EventId}",
                @event.GetPayload().GetType().Name,
                @event.PartitionKeys.AggregateId);
            throw;
        }
        finally
        {
            // Clear event context
            _eventContextProvider.ClearCurrentEventContext();
        }
    }

    /// <summary>
    ///     Process event with handler
    /// </summary>
    private async Task ProcessEventWithHandlerAsync(IReadModelHandler handler, IEvent @event)
    {
        try
        {
            await handler.HandleEventAsync(@event);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error in handler {HandlerType} processing event {EventType} with ID {EventId}",
                handler.GetType().Name,
                @event.GetPayload().GetType().Name,
                @event.PartitionKeys.AggregateId);

            // Error handling policy can be applied here
            // For example: retry, skip, rethrow, etc.
            throw;
        }
    }

    /// <summary>
    ///     Process events
    /// </summary>
    public async Task ProcessEventsAsync(IEnumerable<IEvent> events)
    {
        foreach (var @event in events)
        {
            await ProcessEventAsync(@event);
        }
    }
}
