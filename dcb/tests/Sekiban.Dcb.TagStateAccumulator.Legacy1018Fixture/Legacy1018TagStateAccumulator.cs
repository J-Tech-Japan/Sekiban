using Sekiban.Dcb.Events;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.TagStateAccumulator.Legacy1018Fixture;

/// <summary>
///     A real binary compiled against the published 10.18 batch-only accumulator interface. It intentionally knows
///     nothing about ApplyEvent; the current runtime must dispatch that default member to ApplyEvents exactly once.
/// </summary>
public sealed class Legacy1018TagStateAccumulator : ITagStateProjectionAccumulator
{
    public int ApplyEventsCalls { get; private set; }
    public int LastBatchCount { get; private set; }
    public string? LastHead { get; private set; }

    public bool ApplyState(SerializableTagState? cachedState) => true;

    public bool ApplyEvents(
        IReadOnlyList<SerializableEvent> events,
        string? latestSortableUniqueId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyEventsCalls++;
        LastBatchCount = events.Count;
        LastHead = latestSortableUniqueId;
        return true;
    }

    public SerializableTagState GetSerializedState() =>
        throw new NotSupportedException("The compatibility fixture is only used to exercise the default ApplyEvent member.");

    public void Dispose()
    {
    }
}
