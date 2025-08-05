using DcbLib.Events;
using ResultBoxes;

namespace DcbLib.Storage;

/// <summary>
/// Extension methods for IEventStore
/// </summary>
public static class EventStoreExtensions
{
    /// <summary>
    /// Helper method to write a single event using WriteEventsAsync
    /// </summary>
    public static async Task<ResultBox<Event>> WriteEventAsync(this IEventStore eventStore, Event evt)
    {
        var result = await eventStore.WriteEventsAsync(new[] { evt });
        if (result.IsSuccess)
        {
            var (events, _) = result.GetValue();
            return ResultBox.FromValue(events.First());
        }
        
        return ResultBox.Error<Event>(result.GetException());
    }
}