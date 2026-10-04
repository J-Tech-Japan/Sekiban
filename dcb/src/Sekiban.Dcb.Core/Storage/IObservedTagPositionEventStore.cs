using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Storage;

/// <summary>Optional recording of the tag positions a command observed. The store decides whether to record.</summary>
public interface IObservedTagPositionEventStore
{
    /// <summary>True only when the provider is configured to record observations.</summary>
    bool RecordsObservedTagPositions { get; }

    /// <summary>
    /// Writes events with the original command observations keyed by tag string:
    /// null means not observed, empty means observed empty, otherwise a 30-digit position.
    /// </summary>
    Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>>
        WriteSerializableEventsWithObservedTagPositionsAsync(
            IEnumerable<SerializableEvent> events,
            IReadOnlyDictionary<string, string?> observedTagPositions,
            CancellationToken cancellationToken = default);
}
