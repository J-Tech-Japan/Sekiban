using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.ColdEvents;

public static class ColdSegmentSplitter
{
    public static IReadOnlyList<IReadOnlyList<SerializableEvent>> Split(
        IReadOnlyList<SerializableEvent> events,
        int maxEvents,
        long maxBytes)
    {
        if (events.Count == 0)
        {
            return [];
        }

        var segments = new List<IReadOnlyList<SerializableEvent>>();
        var currentSegment = new List<SerializableEvent>();
        long currentBytes = 0;

        foreach (var e in events)
        {
            long eventSize = e.Payload.Length;

            if (currentSegment.Count > 0 && ShouldRotate(currentSegment.Count, currentBytes, eventSize, maxEvents, maxBytes))
            {
                segments.Add(currentSegment);
                currentSegment = new List<SerializableEvent>();
                currentBytes = 0;
            }

            currentSegment.Add(e);
            currentBytes += eventSize;
        }

        if (currentSegment.Count > 0)
        {
            segments.Add(currentSegment);
        }

        return segments;
    }

    private static bool ShouldRotate(
        int currentCount,
        long currentBytes,
        long nextEventSize,
        int maxEvents,
        long maxBytes)
    {
        return currentCount >= maxEvents
               || currentBytes + nextEventSize > maxBytes;
    }
}
