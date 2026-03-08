using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.ColdEvents;

public interface IColdSegmentFormatHandler
{
    string BuildSegmentPath(string serviceId, string fromSortableUniqueId, string toSortableUniqueId);

    Task<IColdSegmentFileBuilder> CreateBuilderAsync(SerializableEvent firstEvent, CancellationToken ct);

    Task<ResultBox<ColdSegmentStreamResult>> StreamSegmentAsync(
        Stream data,
        SortableUniqueId? since,
        int? maxCount,
        Func<SerializableEvent, ValueTask> onEvent,
        CancellationToken ct);
}

public interface IColdSegmentFileBuilder : IAsyncDisposable
{
    int EventCount { get; }
    string? FromSortableUniqueId { get; }
    string? ToSortableUniqueId { get; }

    bool CanAppend(SerializableEvent evt, ColdEventStoreOptions options);

    Task AppendAsync(SerializableEvent evt, CancellationToken ct);

    Task<ColdSegmentArtifact> CompleteAsync(string serviceId, CancellationToken ct);
}

public sealed record ColdSegmentArtifact(string FilePath, ColdSegmentInfo Info);

public sealed record ColdSegmentStreamResult(
    int EventsRead,
    string? LastSortableUniqueId,
    bool ReachedEndOfSegment);
