using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Storage;

public interface ISerializableEventStreamReader
{
    IAsyncEnumerable<SerializableEvent> StreamAllSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount,
        CancellationToken ct = default);
}
