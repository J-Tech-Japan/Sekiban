using ResultBoxes;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using System.Collections.Concurrent;
namespace Sekiban.Dcb.Testing;

/// <summary>
///     The deterministic in-memory REFERENCE implementation of the full conditional-append outcome machine, placed in the
///     testing package (never referenced from a runtime project) exactly like the other in-memory reference stores.
///     It is a full <see cref="IEventStore" /> (so appended events are readable / projectable) plus
///     <see cref="IConditionalEventStore" />, and it declares the capability via
///     <see cref="IWriteConditionCapabilityProvider" /> so the runtime probe and the cast agree.
///     Semantics, per ServiceId (isolated exactly as the base store isolates events):
///     first claim of a key wins and is written durably (Appended, with a receipt); a later attempt with the SAME
///     operation fingerprint returns the ORIGINAL receipt (AlreadyCommittedSameOperation) and writes nothing; a later
///     attempt with a DIFFERENT fingerprint is a KeyReuseConflict. Conflicts are discovered by READ here, so there is no
///     provider exception and none is fabricated as an inner cause.
/// </summary>
public sealed class InMemoryConditionalEventStore : InMemoryEventStore, IConditionalEventStore, IWriteConditionCapabilityProvider
{
    private const string ProviderName = "InMemoryConditional";
    private readonly IServiceIdProvider _serviceIdProvider;
    private readonly IEventTypes _eventTypes;
    private readonly ConcurrentDictionary<string, ServiceClaims> _claimsByService = new(StringComparer.Ordinal);

    private int _writeCalls;

    public InMemoryConditionalEventStore(IEventTypes eventTypes, IServiceIdProvider? serviceIdProvider = null)
        : base(eventTypes, serviceIdProvider)
    {
        _eventTypes = eventTypes;
        _serviceIdProvider = serviceIdProvider ?? new DefaultServiceIdProvider();
    }

    /// <summary>
    ///     Number of times a conditional append actually reached the durable write (the Appended path). A rejected
    ///     append (fault-gate, canonicalization boundary, or key-reuse) never increments this — so a test can assert the
    ///     forbidden side effect did NOT happen, not merely that the store looks empty.
    /// </summary>
    public int WriteCalls => Volatile.Read(ref _writeCalls);

    public WriteConditionCapabilityDescriptor DescribeWriteConditions() =>
        WriteConditionCapabilityDescriptor.Supporting(ProviderName, WriteConditionKind.SingleEventUniqueKey);

    public Task<ResultBox<ConditionalAppendReceipt>> AppendIfUniqueAsync(
        ConditionalAppendRequest request,
        CancellationToken cancellationToken = default)
    {
        var serviceId = _serviceIdProvider.GetCurrentServiceId();
        string normalizedKey;
        try
        {
            normalizedKey = OperationFingerprint.NormalizeKey(request.IdempotencyKey);
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(ResultBox.Error<ConditionalAppendReceipt>(ex));
        }

        // Canonical, authoritative fingerprint. Fails closed (typed) BEFORE any write when the type is unregistered or
        // the payload cannot be canonicalized.
        var fingerprintResult = OperationFingerprint.ComputeCanonical(
            serviceId,
            request.IdempotencyKey,
            _eventTypes,
            request.Event.EventPayloadName,
            request.Event.Payload,
            request.Event.Tags);
        if (!fingerprintResult.IsSuccess)
        {
            return Task.FromResult(ResultBox.Error<ConditionalAppendReceipt>(fingerprintResult.GetException()));
        }

        var fingerprint = fingerprintResult.GetValue();

        var claims = _claimsByService.GetOrAdd(serviceId, _ => new ServiceClaims());
        lock (claims.Lock)
        {
            if (claims.ByKey.TryGetValue(normalizedKey, out var existing))
            {
                if (string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    return Task.FromResult(
                        ResultBox.FromValue(
                            new ConditionalAppendReceipt(
                                ConditionalAppendStatus.AlreadyCommittedSameOperation,
                                existing.EventId,
                                existing.SortableUniqueId,
                                existing.Fingerprint)));
                }

                // Conflict discovered by read — no provider exception occurred, so none is fabricated.
                return Task.FromResult(
                    ResultBox.Error<ConditionalAppendReceipt>(
                        new KeyReuseConflictException(existing.Fingerprint, fingerprint, ProviderName)));
            }

            // First claim: write the event through the base store, then record the claim. The base write is synchronous,
            // so blocking on it here holds no async gap; the whole (write + claim) is atomic under this lock.
            Interlocked.Increment(ref _writeCalls);
            var writeResult = base.WriteSerializableEventsAsync(new[] { request.Event }).GetAwaiter().GetResult();
            if (!writeResult.IsSuccess)
            {
                return Task.FromResult(ResultBox.Error<ConditionalAppendReceipt>(writeResult.GetException()));
            }

            claims.ByKey[normalizedKey] = new ClaimRecord(
                request.Event.Id,
                request.Event.SortableUniqueIdValue,
                fingerprint);
            return Task.FromResult(
                ResultBox.FromValue(
                    new ConditionalAppendReceipt(
                        ConditionalAppendStatus.Appended,
                        request.Event.Id,
                        request.Event.SortableUniqueIdValue,
                        fingerprint)));
        }
    }

    private sealed class ServiceClaims
    {
        public object Lock { get; } = new();
        public Dictionary<string, ClaimRecord> ByKey { get; } = new(StringComparer.Ordinal);
    }

    private sealed record ClaimRecord(Guid EventId, string SortableUniqueId, string Fingerprint);
}
