using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.SizeGates;

/// <summary>
/// The representation whose size a policy validates. The logical representation is the canonical UTF-8 event
/// envelope produced by the executor. Storage and destination representations require an explicit capability.
/// </summary>
public enum ExecutorSizeRepresentation
{
    LogicalSerializedEventUtf8,
    StorageItem,
    Destination
}

public enum ExecutorSizeStrictness
{
    Strict,
    NonStrict
}

/// <summary>
/// An opt-in size policy. A policy must set at least one positive limit. The scope is an application/provider label
/// such as <c>logical-event</c>, <c>postgres-event-row</c>, or <c>azure-queue-destination</c>; it is never used as a
/// secret-bearing payload field.
/// </summary>
public sealed record ExecutorSizePolicy
{
    public ExecutorSizePolicy(
        string scope,
        ExecutorSizeRepresentation representation,
        long? maxBytesPerEvent = null,
        long? maxBytesPerOperation = null,
        ExecutorSizeStrictness strictness = ExecutorSizeStrictness.Strict,
        IExecutorSizeMeasurement? measurement = null)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Representation = representation;
        MaxBytesPerEvent = maxBytesPerEvent;
        MaxBytesPerOperation = maxBytesPerOperation;
        Strictness = strictness;
        Measurement = measurement;
    }

    public string Scope { get; }
    public ExecutorSizeRepresentation Representation { get; }
    public long? MaxBytesPerEvent { get; }
    public long? MaxBytesPerOperation { get; }
    public ExecutorSizeStrictness Strictness { get; }
    public IExecutorSizeMeasurement? Measurement { get; }
}

/// <summary>Mutable registration object used by explicit constructors and dependency injection.</summary>
public sealed class ExecutorSizeGateOptions
{
    public IList<ExecutorSizePolicy> Policies { get; } = new List<ExecutorSizePolicy>();

    public ExecutorSizeGateOptions Add(ExecutorSizePolicy policy)
    {
        Policies.Add(policy ?? throw new ArgumentNullException(nameof(policy)));
        return this;
    }

    internal void Validate()
    {
        var scopes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var policy in Policies)
        {
            if (string.IsNullOrWhiteSpace(policy.Scope))
            {
                throw new ArgumentException("Executor size policy scope must not be empty.", nameof(Policies));
            }

            if (!scopes.Add(policy.Scope))
            {
                throw new ArgumentException(
                    $"Executor size policy scope '{policy.Scope}' is registered more than once.",
                    nameof(Policies));
            }

            if ((policy.MaxBytesPerEvent is not null && policy.MaxBytesPerEvent <= 0) ||
                (policy.MaxBytesPerOperation is not null && policy.MaxBytesPerOperation <= 0) ||
                (policy.MaxBytesPerEvent is null && policy.MaxBytesPerOperation is null))
            {
                throw new ArgumentException(
                    $"Executor size policy '{policy.Scope}' must declare at least one positive byte limit.",
                    nameof(Policies));
            }
        }
    }
}

/// <summary>
/// Provider-neutral measurement hook. Implementations must return an exact byte count or a certified conservative
/// upper bound for the declared representation; returning raw logical bytes for a storage/destination claim is not a
/// valid capability.
/// </summary>
public interface IExecutorSizeMeasurement
{
    ExecutorSizeMeasurementResult Measure(ExecutorSizeMeasurementContext context);
}

public sealed record ExecutorSizeMeasurementContext(
    string Scope,
    ExecutorSizeRepresentation Representation,
    Event Event,
    SerializableEvent SerializedEvent,
    string ServiceId,
    string? DestinationKey,
    ExecutorSizeDestinationPlan? DestinationPlan);

public sealed record ExecutorSizeMeasurementResult(
    bool IsAvailable,
    long? Bytes,
    long? CertifiedUpperBound,
    string? Reason)
{
    public long? ComparableBytes => Bytes ?? CertifiedUpperBound;

    public static ExecutorSizeMeasurementResult Exact(long bytes) =>
        new(true, bytes, null, null);

    public static ExecutorSizeMeasurementResult CertifiedBound(long bytes) =>
        new(true, null, bytes, null);

    public static ExecutorSizeMeasurementResult Unavailable(string reason) =>
        new(false, null, null, reason);
}

/// <summary>
/// A destination plan captured by the same publisher/resolver that will publish the event. Provider state is opaque to
/// Core and is consumed only by the publisher that created it.
/// </summary>
public sealed record ExecutorSizeDestinationPlan(
    string ServiceId,
    IReadOnlyList<string> DestinationKeys,
    object? ProviderState = null);

/// <summary>
/// Optional additive publisher capability used by destination policies. It both captures the publication plan and
/// accepts that captured plan, preventing a resolver change between validation and enqueue.
/// </summary>
public interface IExecutorSizeDestinationPublisher
{
    ExecutorSizeDestinationPlan? CaptureDestinationPlan(
        Event @event,
        IReadOnlyCollection<ITag> tags,
        string serviceId);

    Task PublishAsync(
        IReadOnlyCollection<(Event Event, IReadOnlyCollection<ITag> Tags)> events,
        IReadOnlyDictionary<Guid, ExecutorSizeDestinationPlan> destinationPlans,
        CancellationToken cancellationToken = default);
}

public sealed record ExecutorSizeDiagnostic(
    string Scope,
    ExecutorSizeRepresentation Representation,
    string Reason);

public sealed class ExecutorSizeLimitExceededException : Exception
{
    public ExecutorSizeLimitExceededException(
        string scope,
        ExecutorSizeRepresentation representation,
        long limitBytes,
        long measuredBytes,
        Guid eventId,
        int eventIndex,
        bool operationLimit,
        string? destinationKey = null,
        bool certifiedUpperBound = false)
        : base(
            $"Executor size policy '{scope}' rejected event {eventId} at index {eventIndex}: " +
            $"{representation} {(operationLimit ? "operation" : "event")} size {measuredBytes} bytes " +
            $"exceeds limit {limitBytes} bytes.")
    {
        Scope = scope;
        Representation = representation;
        LimitBytes = limitBytes;
        MeasuredBytes = measuredBytes;
        EventId = eventId;
        EventIndex = eventIndex;
        IsOperationLimit = operationLimit;
        DestinationKey = destinationKey;
        IsCertifiedUpperBound = certifiedUpperBound;
    }

    public string Scope { get; }
    public ExecutorSizeRepresentation Representation { get; }
    public long LimitBytes { get; }
    public long MeasuredBytes { get; }
    public Guid EventId { get; }
    public int EventIndex { get; }
    public bool IsOperationLimit { get; }
    public string? DestinationKey { get; }
    public bool IsCertifiedUpperBound { get; }
}

public sealed class ExecutorSizeCapabilityException : Exception
{
    public ExecutorSizeCapabilityException(
        string scope,
        ExecutorSizeRepresentation representation,
        string reason)
        : base($"Executor size policy '{scope}' cannot validate {representation}: {reason}")
    {
        Scope = scope;
        Representation = representation;
        Reason = reason;
    }

    public string Scope { get; }
    public ExecutorSizeRepresentation Representation { get; }
    public string Reason { get; }
}
