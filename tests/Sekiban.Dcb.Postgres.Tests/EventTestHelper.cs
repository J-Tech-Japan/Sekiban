using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Postgres.Tests;

public static class EventTestHelper
{
    public static Event CreateEvent(IEventPayload payload, params ITag[] tags)
    {
        return new Event(
            payload,
            SortableUniqueId.GenerateNew(),
            payload.GetType().Name,
            Guid.NewGuid(),
            new EventMetadata(
                Guid.NewGuid().ToString(),
                Guid.NewGuid().ToString(),
                "test-user"
            ),
            tags.Select(t => t.GetTag()).ToList()
        );
    }
}