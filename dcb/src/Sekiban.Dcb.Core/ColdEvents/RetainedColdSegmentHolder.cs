using System.Collections.Concurrent;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.ColdEvents;

// Shared across factory lifetimes and scopes within one service provider.
internal sealed class RetainedColdSegmentHolders
{
    private readonly ConcurrentDictionary<string, RetainedColdSegmentHolder> _holders = new(StringComparer.Ordinal);

    internal RetainedColdSegmentHolder Get(string normalizedServiceId)
        => _holders.GetOrAdd(normalizedServiceId, _ => new RetainedColdSegmentHolder());
}

// One immutable suffix per holder. Readers keep their snapshot even if another read replaces it.
internal sealed class RetainedColdSegmentHolder
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);
    private RetainedColdSegment? _content;

    internal RetainedColdSegment? Get(DateTimeOffset now)
    {
        var content = Volatile.Read(ref _content);
        if (content is not null && now - content.LastUsed >= IdleTimeout)
        {
            Clear(content);
            return null;
        }
        return content;
    }

    internal void Replace(RetainedColdSegment content) => Interlocked.Exchange(ref _content, content);

    internal void Touch(RetainedColdSegment content, DateTimeOffset now)
    {
        var current = Volatile.Read(ref _content);
        while (current is not null && ReferenceEquals(current.Events, content.Events))
        {
            var updated = current with { LastUsed = now };
            var observed = Interlocked.CompareExchange(ref _content, updated, current);
            if (ReferenceEquals(observed, current)) return;
            current = observed;
        }
    }

    internal void Clear(RetainedColdSegment? content)
    {
        if (content is null) return;
        var current = Volatile.Read(ref _content);
        while (current is not null && ReferenceEquals(current.Events, content.Events))
        {
            var observed = Interlocked.CompareExchange(ref _content, null, current);
            if (ReferenceEquals(observed, current)) return;
            current = observed;
        }
    }
}

internal sealed record RetainedColdSegment(
    string ServiceId,
    ColdSegmentInfo Entry,
    string LoadedFrom,
    IReadOnlyList<SerializableEvent> Events,
    bool IsSorted,
    int SegmentCount,
    string ColdBoundary,
    DateTimeOffset LastUsed);
