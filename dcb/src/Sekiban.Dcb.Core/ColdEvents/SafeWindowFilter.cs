using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.ColdEvents;

public static class SafeWindowFilter
{
    public static IReadOnlyList<SerializableEvent> Apply(
        IEnumerable<SerializableEvent> events,
        DateTime cutoffUtc)
    {
        return events
            .Where(e => IsSafe(e, cutoffUtc))
            .ToList();
    }

    private static bool IsSafe(SerializableEvent e, DateTime cutoffUtc)
    {
        var eventTime = new SortableUniqueId(e.SortableUniqueIdValue).GetDateTime();
        return eventTime <= cutoffUtc;
    }
}
