using ResultBoxes;
using Sekiban.Pure.Events;
namespace Pure.Domain.xUnit;

public static class EventReaderExtensions
{
    public static async Task<IReadOnlyList<IEvent>> GetAllEventsAsync(this IEventReader eventReader)
    {
        var result = await eventReader.GetEvents(EventRetrievalInfo.All);
        return result.UnwrapBox();
    }
}
