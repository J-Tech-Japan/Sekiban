using ResultBoxes;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Storage;

/// <summary>
///     Carries the SEK-G16 conditional-append boilerplate that is identical for every provider — the capability
///     descriptor and the delegation into <see cref="ConditionalAppendExecution.RunAsync" /> — so a provider event store
///     holds one of these and forwards to it instead of repeating the same scaffold. The provider supplies only what is
///     genuinely provider-specific: its name, how to read the current ServiceId, its event types, and the two storage
///     primitives (durably write the claim under a deterministic id, and read the committed winner back).
/// </summary>
public sealed class ConditionalAppendCoordinator
{
    private readonly string _providerName;
    private readonly Func<string> _currentServiceId;
    private readonly IEventTypes _eventTypes;
    private readonly Func<Guid, SerializableEvent, CancellationToken, Task<ConditionalWriteOutcome>> _tryWriteClaim;
    private readonly Func<Guid, CancellationToken, Task<SerializableEvent?>> _readCommittedWinner;
    private readonly Func<SerializableEvent, CancellationToken, Task>? _ensureCommittedAsync;

    /// <param name="ensureCommittedAsync">
    ///     Optional. On a same-operation retry, brings the winner's contracted committed state (e.g. every required tag
    ///     row) to convergence before AlreadyCommitted is returned. Supply it on a store whose event and tag rows are NOT
    ///     written atomically (Cosmos); leave null where the write is atomic (Postgres/SQLite/DynamoDB transaction).
    /// </param>
    public ConditionalAppendCoordinator(
        string providerName,
        Func<string> currentServiceId,
        IEventTypes eventTypes,
        Func<Guid, SerializableEvent, CancellationToken, Task<ConditionalWriteOutcome>> tryWriteClaim,
        Func<Guid, CancellationToken, Task<SerializableEvent?>> readCommittedWinner,
        Func<SerializableEvent, CancellationToken, Task>? ensureCommittedAsync = null)
    {
        _providerName = providerName;
        _currentServiceId = currentServiceId;
        _eventTypes = eventTypes;
        _tryWriteClaim = tryWriteClaim;
        _readCommittedWinner = readCommittedWinner;
        _ensureCommittedAsync = ensureCommittedAsync;
    }

    /// <summary>
    ///     Test seam ONLY (never set in production): overrides the bounded post-ambiguity verification budget so a provider
    ///     path's timeout can be exercised deterministically without waiting the production default. Null → the default.
    /// </summary>
    internal TimeSpan? VerificationBudgetOverride { get; set; }

    /// <summary>The single-event unique-key capability descriptor for this provider.</summary>
    public WriteConditionCapabilityDescriptor Descriptor =>
        WriteConditionCapabilityDescriptor.Supporting(_providerName, WriteConditionKind.SingleEventUniqueKey);

    /// <summary>Runs the shared conditional-append outcome machine with this provider's primitives.</summary>
    public Task<ResultBox<ConditionalAppendReceipt>> AppendIfUniqueAsync(
        ConditionalAppendRequest request,
        CancellationToken cancellationToken) =>
        ConditionalAppendExecution.RunAsync(
            request,
            _currentServiceId(),
            _eventTypes,
            _providerName,
            _tryWriteClaim,
            _readCommittedWinner,
            _ensureCommittedAsync,
            cancellationToken,
            VerificationBudgetOverride);
}
