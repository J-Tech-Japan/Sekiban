using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Orleans;

/// <summary>Validated bounds used by the Orleans notification publisher.</summary>
public sealed class OrleansEventPublisherOptions
{
    public int MaxPublishAttempts { get; set; } = 5;
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public int MaxQueuedItemsPerDestination { get; set; } = 1024;
    public int MaxQueuedItemsTotal { get; set; } = 16_384;
    public int TerminalSampleCount { get; set; } = 20;
    public int MaxDiagnosticDestinations { get; set; } = 1024;

    public void Validate()
    {
        if (MaxPublishAttempts is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(MaxPublishAttempts));
        if (BaseRetryDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(BaseRetryDelay));
        if (MaxRetryDelay < BaseRetryDelay || MaxRetryDelay > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(MaxRetryDelay));
        if (MaxQueuedItemsPerDestination is < 1 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(MaxQueuedItemsPerDestination));
        if (MaxQueuedItemsTotal < MaxQueuedItemsPerDestination || MaxQueuedItemsTotal > 1_048_576)
            throw new ArgumentOutOfRangeException(nameof(MaxQueuedItemsTotal));
        if (TerminalSampleCount is < 1 or > 1_000)
            throw new ArgumentOutOfRangeException(nameof(TerminalSampleCount));
        if (MaxDiagnosticDestinations is < 1 or > 65_536)
            throw new ArgumentOutOfRangeException(nameof(MaxDiagnosticDestinations));
    }
}

/// <summary>Bounded, payload-free diagnostics for notifications that were attempted or rejected.</summary>
public interface IOrleansPublisherDiagnostics
{
    OrleansPublisherDiagnosticsSnapshot GetSnapshot();
}

public sealed class OrleansPublisherDiagnosticsSnapshot
{
    internal OrleansPublisherDiagnosticsSnapshot(
        long totalTerminalCount,
        long totalAttemptCount,
        IReadOnlyDictionary<string, long> reasonCounts,
        long evictedDestinationCount,
        IReadOnlyList<OrleansPublisherDestinationDiagnostic> destinations)
    {
        TotalTerminalCount = totalTerminalCount;
        TotalAttemptCount = totalAttemptCount;
        ReasonCounts = reasonCounts;
        EvictedDestinationCount = evictedDestinationCount;
        Destinations = destinations;
    }

    public long TotalTerminalCount { get; }
    public long TotalAttemptCount { get; }
    public IReadOnlyDictionary<string, long> ReasonCounts { get; }
    public long EvictedDestinationCount { get; }
    public IReadOnlyList<OrleansPublisherDestinationDiagnostic> Destinations { get; }
}

public sealed class OrleansPublisherDestinationDiagnostic
{
    internal OrleansPublisherDestinationDiagnostic(
        string destinationKey,
        string serviceId,
        string provider,
        string @namespace,
        Guid streamId,
        long terminalCount,
        long attemptCount,
        IReadOnlyDictionary<string, long> reasonCounts,
        IReadOnlyList<OrleansPublisherTerminalSample> samples)
    {
        DestinationKey = destinationKey;
        ServiceId = serviceId;
        Provider = provider;
        Namespace = @namespace;
        StreamId = streamId;
        TerminalCount = terminalCount;
        AttemptCount = attemptCount;
        ReasonCounts = reasonCounts;
        Samples = samples;
    }

    public string DestinationKey { get; }
    public string ServiceId { get; }
    public string Provider { get; }
    public string Namespace { get; }
    public Guid StreamId { get; }
    public long TerminalCount { get; }
    public long AttemptCount { get; }
    public IReadOnlyDictionary<string, long> ReasonCounts { get; }
    public IReadOnlyList<OrleansPublisherTerminalSample> Samples { get; }
}

public sealed class OrleansPublisherTerminalSample
{
    internal OrleansPublisherTerminalSample(Guid eventId, string reasonCode, int attempts, DateTimeOffset occurredAtUtc)
    {
        EventId = eventId;
        ReasonCode = reasonCode;
        Attempts = attempts;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid EventId { get; }
    public string ReasonCode { get; }
    public int Attempts { get; }
    public DateTimeOffset OccurredAtUtc { get; }
}

internal sealed record OrleansPublishDestination(
    string ServiceId,
    string ProviderName,
    string StreamNamespace,
    Guid StreamId)
{
    public string DestinationKey =>
        $"{ServiceId}|{ProviderName}|{StreamNamespace}|{StreamId:D}";
}

/// <summary>One immutable prepared event and context shared by all retries and destination work items.</summary>
internal sealed class SharedPublishPayload
{
    private int _references = 1;
    private readonly Action? _released;

    public SharedPublishPayload(
        SerializableEvent @event,
        IReadOnlyDictionary<string, object>? requestContext,
        Action? released = null)
    {
        Event = @event;
        RequestContext = requestContext is null
            ? null
            : requestContext as Dictionary<string, object> ??
              new Dictionary<string, object>(requestContext, StringComparer.Ordinal);
        _released = released;
    }

    public SerializableEvent Event { get; }
    public Dictionary<string, object>? RequestContext { get; }
    internal int ReferenceCount => Volatile.Read(ref _references);

    public void Retain()
    {
        while (true)
        {
            var current = Volatile.Read(ref _references);
            if (current == 0)
                throw new ObjectDisposedException(nameof(SharedPublishPayload));
            if (Interlocked.CompareExchange(ref _references, current + 1, current) == current)
                return;
        }
    }

    public void Release()
    {
        var remaining = Interlocked.Decrement(ref _references);
        if (remaining < 0)
            throw new InvalidOperationException("Shared publish payload was released too many times.");
        if (remaining == 0)
            _released?.Invoke();
    }
}

internal sealed record OrleansPublishItem(
    OrleansDestinationPlanState Destination,
    SharedPublishPayload Payload);

/// <summary>Prepared provider/stream target retained by one immutable destination plan state.</summary>
internal interface IOrleansPreparedStreamTarget
{
    Task SendAsync(SharedPublishPayload payload, CancellationToken cancellationToken);
}

/// <summary>Internal sender seam; the production implementation prepares the Orleans stream adapter once.</summary>
internal interface IOrleansStreamSender
{
    IOrleansPreparedStreamTarget PrepareDestination(OrleansPublishDestination destination);
}

internal sealed class OrleansPublisherDiagnosticsRecorder
{
    private readonly object _gate = new();
    private readonly int _sampleCount;
    private readonly int _maxDestinations;
    private readonly Dictionary<string, MutableDestination> _destinations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _reasonCounts = new(StringComparer.Ordinal);
    private long _totalTerminalCount;
    private long _totalAttemptCount;
    private long _evictedDestinationCount;

    public OrleansPublisherDiagnosticsRecorder(OrleansEventPublisherOptions options)
    {
        _sampleCount = options.TerminalSampleCount;
        _maxDestinations = options.MaxDiagnosticDestinations;
    }

    public void Attempt(OrleansPublishDestination destination)
    {
        lock (_gate)
        {
            _totalAttemptCount++;
            if (TryGetDestination(destination, out var state))
                state.AttemptCount++;
        }
    }

    public void Terminal(
        OrleansPublishDestination? destination,
        Guid eventId,
        string reasonCode,
        int attempts)
    {
        lock (_gate)
        {
            _totalTerminalCount++;
            Increment(_reasonCounts, reasonCode);
            if (destination is null || !TryGetDestination(destination, out var state))
                return;

            state.TerminalCount++;
            Increment(state.ReasonCounts, reasonCode);
            if (state.Samples.Count < _sampleCount)
            {
                state.Samples.Add(new OrleansPublisherTerminalSample(
                    eventId,
                    reasonCode,
                    attempts,
                    DateTimeOffset.UtcNow));
            }
        }
    }

    public OrleansPublisherDiagnosticsSnapshot Snapshot()
    {
        lock (_gate)
        {
            var destinations = _destinations.Values
                .OrderBy(value => value.DestinationKey, StringComparer.Ordinal)
                .Select(value => new OrleansPublisherDestinationDiagnostic(
                    value.DestinationKey,
                    value.ServiceId,
                    value.Provider,
                    value.Namespace,
                    value.StreamId,
                    value.TerminalCount,
                    value.AttemptCount,
                    new Dictionary<string, long>(value.ReasonCounts, StringComparer.Ordinal),
                    value.Samples.ToArray()))
                .ToArray();
            return new OrleansPublisherDiagnosticsSnapshot(
                _totalTerminalCount,
                _totalAttemptCount,
                new Dictionary<string, long>(_reasonCounts, StringComparer.Ordinal),
                _evictedDestinationCount,
                destinations);
        }
    }

    private bool TryGetDestination(OrleansPublishDestination destination, out MutableDestination state)
    {
        if (_destinations.TryGetValue(destination.DestinationKey, out state!))
            return true;

        if (_destinations.Count >= _maxDestinations)
        {
            var evicted = _destinations.Keys.OrderBy(key => key, StringComparer.Ordinal).First();
            _destinations.Remove(evicted);
            _evictedDestinationCount++;
        }

        state = new MutableDestination(
            destination.DestinationKey,
            destination.ServiceId,
            destination.ProviderName,
            destination.StreamNamespace,
            destination.StreamId);
        _destinations.Add(destination.DestinationKey, state);
        return true;
    }

    private static void Increment(Dictionary<string, long> counts, string reasonCode)
    {
        counts[reasonCode] = counts.TryGetValue(reasonCode, out var count) ? count + 1 : 1;
    }

    private sealed class MutableDestination(
        string destinationKey,
        string serviceId,
        string provider,
        string @namespace,
        Guid streamId)
    {
        public string DestinationKey { get; } = destinationKey;
        public string ServiceId { get; } = serviceId;
        public string Provider { get; } = provider;
        public string Namespace { get; } = @namespace;
        public Guid StreamId { get; } = streamId;
        public long TerminalCount { get; set; }
        public long AttemptCount { get; set; }
        public Dictionary<string, long> ReasonCounts { get; } = new(StringComparer.Ordinal);
        public List<OrleansPublisherTerminalSample> Samples { get; } = [];
    }
}
