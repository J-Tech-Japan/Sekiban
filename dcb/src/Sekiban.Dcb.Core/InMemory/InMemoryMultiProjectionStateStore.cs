using System.Collections.Concurrent;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Storage.Checkpoints;

namespace Sekiban.Dcb.InMemory;

/// <summary>
///     In-memory implementation for tests. SEK-G20: this is also the deterministic REFERENCE implementation of the
///     generation/tombstone/exact-token CAS state machine (<see cref="IGenerationAwareCheckpointStore" />). All control-
///     plane transitions run under a single gate so concurrent writers observe exact-token CAS semantics.
/// </summary>
[Obsolete(
    "Moved to Sekiban.Dcb.Core.Testing (namespace Sekiban.Dcb.Testing). This type is volatile/in-process and is for tests only; it lives in a production package for historical reasons, which is how it reached production once. Behaviour is unchanged and it will not be removed before the next major version.")]
public class InMemoryMultiProjectionStateStore :
    IMultiProjectionStateStore,
    IProjectionStatusStore,
    IStorageDurabilityDescriptorProvider,
    IGenerationAwareCheckpointStore
{
    private readonly ConcurrentDictionary<(string ServiceId, string ProjectorName, string ProjectorVersion), MultiProjectionStateRecord> _states = new();
    private readonly ConcurrentDictionary<(string ServiceId, string ProjectorName, string ProjectorVersion), byte[]> _stateData = new();
    // One CAS row per service/projector/version/cluster. ActivationId is data in the row, not part of its identity;
    // this is what lets a replacement activation observe and fence the previous writer.
    private readonly Dictionary<(string ServiceId, string ProjectorName, string ProjectorVersion, string ClusterId), ProjectionStatusHeartbeat> _statusRows = new();

    // SEK-G20 control plane: generation (rebuild epoch) + monotonic revision (exact-CAS token) + lifecycle. Guarded by
    // _casGate so every read-modify-write is atomic — the reference for what a native provider CAS must guarantee.
    private readonly Dictionary<(string ServiceId, string ProjectorName, string ProjectorVersion), ControlEntry> _control = new();
    private readonly object _casGate = new();
    private readonly IServiceIdProvider _serviceIdProvider;

    private sealed record ControlEntry(long Generation, long Revision, CheckpointLifecycle Lifecycle);

    public InMemoryMultiProjectionStateStore(IServiceIdProvider? serviceIdProvider = null)
    {
        _serviceIdProvider = serviceIdProvider ?? new DefaultServiceIdProvider();
    }

    /// <summary>Projection state held in this process only: rebuildable from events, but not itself durable.</summary>
    public StorageDurabilityDescriptor DescribeStorage() =>
        new(StorageDurability.Volatile, "InMemory");

    private string CurrentServiceId => _serviceIdProvider.GetCurrentServiceId();

    /// <summary>
    ///     Clear all stored states. Used for test isolation.
    /// </summary>
    public void Clear()
    {
        var serviceId = CurrentServiceId;
        var keysToRemove = _states.Keys.Where(k => k.ServiceId == serviceId).ToList();
        foreach (var key in keysToRemove)
        {
            _states.TryRemove(key, out _);
            _stateData.TryRemove(key, out _);
        }
        lock (_casGate)
        {
            foreach (var key in _control.Keys.Where(k => k.ServiceId == serviceId).ToList())
            {
                _control.Remove(key);
            }

            foreach (var key in _statusRows.Keys.Where(k => k.ServiceId == serviceId).ToList())
            {
                _statusRows.Remove(key);
            }
        }
    }

    public Task<ResultBox<ProjectionStatusWriteResult>> UpsertAsync(
        ProjectionStatusHeartbeat heartbeat,
        long expectedSequence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);
        cancellationToken.ThrowIfCancellationRequested();

        var serviceId = CurrentServiceId;
        if (!string.Equals(heartbeat.ServiceId, serviceId, StringComparison.Ordinal))
        {
            return Task.FromResult(ResultBox.Error<ProjectionStatusWriteResult>(
                new UnauthorizedAccessException(
                    $"Projection status heartbeat belongs to service '{heartbeat.ServiceId}', not '{serviceId}'.")));
        }

        if (string.IsNullOrWhiteSpace(heartbeat.ProjectorName) ||
            string.IsNullOrWhiteSpace(heartbeat.ProjectorVersion) ||
            string.IsNullOrWhiteSpace(heartbeat.ClusterId) ||
            string.IsNullOrWhiteSpace(heartbeat.ActivationId) ||
            heartbeat.Sequence <= 0)
        {
            return Task.FromResult(ResultBox.Error<ProjectionStatusWriteResult>(
                new ArgumentException("Projection status heartbeat identity and sequence are required.")));
        }

        var key = (
            serviceId,
            heartbeat.ProjectorName,
            heartbeat.ProjectorVersion,
            heartbeat.ClusterId);

        lock (_casGate)
        {
            _statusRows.TryGetValue(key, out var current);
            var currentSequence = current?.Sequence ?? 0;
            if (currentSequence != expectedSequence || heartbeat.Sequence <= currentSequence)
            {
                return Task.FromResult(ResultBox.FromValue(
                    ProjectionStatusWriteResult.Rejected(
                        current,
                        $"Heartbeat CAS rejected: expected sequence {expectedSequence}, current sequence {currentSequence}.")));
            }

            _statusRows[key] = heartbeat;
            return Task.FromResult(ResultBox.FromValue(ProjectionStatusWriteResult.Success(heartbeat)));
        }
    }

    public Task<ResultBox<IReadOnlyList<ProjectionStatusHeartbeat>>> ListAsync(
        string? projectorName = null,
        string? projectorVersion = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var serviceId = CurrentServiceId;
        lock (_casGate)
        {
            var rows = _statusRows
                .Where(pair => pair.Key.ServiceId == serviceId)
                .Select(pair => pair.Value)
                .Where(row => projectorName is null || string.Equals(row.ProjectorName, projectorName, StringComparison.Ordinal))
                .Where(row => projectorVersion is null || string.Equals(row.ProjectorVersion, projectorVersion, StringComparison.Ordinal))
                .OrderBy(row => row.ProjectorName, StringComparer.Ordinal)
                .ThenBy(row => row.ProjectorVersion, StringComparer.Ordinal)
                .ThenBy(row => row.ClusterId, StringComparer.Ordinal)
                .ThenBy(row => row.ActivationId, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult(ResultBox.FromValue<IReadOnlyList<ProjectionStatusHeartbeat>>(rows));
        }
    }

    public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        var serviceId = CurrentServiceId;
        _states.TryGetValue((serviceId, projectorName, projectorVersion), out var record);
        return Task.FromResult(ResultBox.FromValue(
            record != null ? OptionalValue.FromValue(record) : OptionalValue<MultiProjectionStateRecord>.Empty));
    }

    public Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(
        string projectorName,
        CancellationToken cancellationToken = default)
    {
        var serviceId = CurrentServiceId;
        var record = _states
            .Where(kvp => kvp.Key.ServiceId == serviceId && kvp.Value.ProjectorName == projectorName)
            .Select(kvp => kvp.Value)
            .OrderByDescending(s => s.EventsProcessed)
            .ThenByDescending(s => s.LastSortableUniqueId)
            .FirstOrDefault();

        return Task.FromResult(ResultBox.FromValue(
            record != null ? OptionalValue.FromValue(record) : OptionalValue<MultiProjectionStateRecord>.Empty));
    }

    public Task<ResultBox<bool>> UpsertAsync(
        MultiProjectionStateRecord record,
        int offloadThresholdBytes = 1_000_000,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ResultBox.Error<bool>(
            new NotSupportedException(
                "InMemoryMultiProjectionStateStore requires payload stream upsert. Use UpsertFromStreamAsync.")));
    }

    public async Task<ResultBox<bool>> UpsertFromStreamAsync(
        MultiProjectionStateWriteRequest request,
        Stream stream,
        int offloadThresholdBytes,
        CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        var bytes = ms.ToArray();

        var serviceId = CurrentServiceId;
        var key = (serviceId, request.ProjectorName, request.ProjectorVersion);
        lock (_casGate)
        {
            _states[key] = request.ToRecord();
            _stateData[key] = bytes;
            // Legacy unconditional upsert == an Active write: keep the control plane consistent (generation preserved,
            // revision advanced) so a later CAS read/observe is coherent. Existing rows default to generation 0.
            var generation = _control.TryGetValue(key, out var existing) ? existing.Generation : 0;
            var revision = existing is null ? 1 : existing.Revision + 1;
            _control[key] = new ControlEntry(generation, revision, CheckpointLifecycle.Active);
        }
        return ResultBox.FromValue(true);
    }

    public Task<ResultBox<Stream>> OpenStateDataReadStreamAsync(
        MultiProjectionStateRecord record,
        CancellationToken cancellationToken = default)
    {
        var serviceId = CurrentServiceId;
        var key = (serviceId, record.ProjectorName, record.ProjectorVersion);
        if (_stateData.TryGetValue(key, out var data))
        {
            return Task.FromResult(ResultBox.FromValue<Stream>(new MemoryStream(data, writable: false)));
        }

        return Task.FromResult(ResultBox.Error<Stream>(
            new InvalidOperationException(
                $"InMemory snapshot payload not found for {record.ProjectorName}/{record.ProjectorVersion}")));
    }

    public Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(
        CancellationToken cancellationToken = default)
    {
        var serviceId = CurrentServiceId;
        var list = _states
            .Where(kvp => kvp.Key.ServiceId == serviceId)
            .Select(kvp => kvp.Value)
            .Select(s => new ProjectorStateInfo(
                s.ProjectorName,
                s.ProjectorVersion,
                s.EventsProcessed,
                s.UpdatedAt,
                s.OriginalSizeBytes,
                s.CompressedSizeBytes,
                s.LastSortableUniqueId))
            .ToList();

        return Task.FromResult(ResultBox.FromValue<IReadOnlyList<ProjectorStateInfo>>(list));
    }

    public Task<ResultBox<bool>> DeleteAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        var serviceId = CurrentServiceId;
        var key = (serviceId, projectorName, projectorVersion);
        var removed = _states.TryRemove(key, out _);
        _stateData.TryRemove(key, out _);
        lock (_casGate)
        {
            _control.Remove(key);
        }
        return Task.FromResult(ResultBox.FromValue(removed));
    }

    public Task<ResultBox<int>> DeleteAllAsync(
        string? projectorName = null,
        CancellationToken cancellationToken = default)
    {
        var serviceId = CurrentServiceId;
        var keysToRemove = string.IsNullOrEmpty(projectorName)
            ? _states.Keys.Where(k => k.ServiceId == serviceId).ToList()
            : _states.Keys.Where(k => k.ServiceId == serviceId && k.ProjectorName == projectorName).ToList();

        foreach (var key in keysToRemove)
        {
            _states.TryRemove(key, out _);
            _stateData.TryRemove(key, out _);
        }
        lock (_casGate)
        {
            foreach (var key in keysToRemove)
            {
                _control.Remove(key);
            }
        }

        return Task.FromResult(ResultBox.FromValue(keysToRemove.Count));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // SEK-G20 generation/tombstone/exact-token CAS (reference implementation)
    // ---------------------------------------------------------------------------------------------------------------

    public CheckpointStoreCapabilityDescriptor DescribeCheckpointCapability() =>
        CheckpointStoreCapabilityDescriptor.Supporting("InMemory", CheckpointCapabilityKind.GenerationTombstoneCas);

    private CheckpointSlot BuildSlotUnlocked((string, string, string) key)
    {
        if (!_control.TryGetValue(key, out var entry))
        {
            return CheckpointSlot.Absent;
        }
        _states.TryGetValue(key, out var record);
        return new CheckpointSlot(true, entry.Generation, entry.Revision.ToString(), entry.Lifecycle, record);
    }

    public Task<ResultBox<CheckpointSlot>> ReadCheckpointSlotAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        var key = (CurrentServiceId, projectorName, projectorVersion);
        lock (_casGate)
        {
            return Task.FromResult(ResultBox.FromValue(BuildSlotUnlocked(key)));
        }
    }

    /// <summary>Whether the observed expectation exactly matches the current control entry (or absence).</summary>
    private bool ExpectationHoldsUnlocked(
        CheckpointExpectation expectation,
        (string, string, string) key,
        out CheckpointSlot current,
        out ControlEntry? entry)
    {
        current = BuildSlotUnlocked(key);
        _control.TryGetValue(key, out entry);
        if (expectation.ExpectAbsent)
        {
            return !current.Exists;
        }
        return current.Exists
            && current.Generation == expectation.ExpectedGeneration
            && string.Equals(current.Revision, expectation.ExpectedRevision, StringComparison.Ordinal)
            && current.Lifecycle == expectation.ExpectedLifecycle;
    }

    private static bool WouldOverflow(long value) => value == long.MaxValue;

    // SEK-G20 test seams for the post-commit ambiguity contract (never set in production). One-shot: consumed by the next
    // ConditionalUpsertAsync. PreCommitFault => the write is rolled back (row unchanged) and the failure is known-safe.
    // PostCommitResponseLoss => the write IS applied but its response is "lost", so the resolver's bounded re-read
    // confirms our own commit. PostCommitResponseLossUnverifiable => the write is NOT applied and its response is lost,
    // so the re-read cannot confirm and the outcome is InDoubt. PostCommitRereadUnavailable => the write IS applied (it
    // crossed the commit boundary) but EVERY bounded re-read throws (the authority is unreachable/timing out); the commit
    // is therefore unknown and MUST remain InDoubt — never downgraded to ProviderFailure just because reads fail.
    internal enum WriteFaultMode
    {
        None,
        PreCommitFault,
        PostCommitResponseLoss,
        PostCommitResponseLossUnverifiable,
        PostCommitRereadUnavailable
    }
    internal WriteFaultMode NextConditionalUpsertFault { get; set; } = WriteFaultMode.None;

    private async Task<(CheckpointCasOutcome? Rejected, byte[] Bytes)> BufferAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
        return (null, ms.ToArray());
    }

    public async Task<CheckpointCasOutcome> ConditionalUpsertAsync(
        MultiProjectionStateWriteRequest payload,
        Stream stream,
        CheckpointExpectation expectation,
        int offloadThresholdBytes,
        CancellationToken cancellationToken = default)
    {
        var (_, bytes) = await BufferAsync(stream, cancellationToken).ConfigureAwait(false);
        var key = (CurrentServiceId, payload.ProjectorName, payload.ProjectorVersion);
        var fault = NextConditionalUpsertFault;
        NextConditionalUpsertFault = WriteFaultMode.None;

        long generation;
        long nextRevision;
        CheckpointSlot committedSlot;
        lock (_casGate)
        {
            if (!ExpectationHoldsUnlocked(expectation, key, out var current, out var entry))
            {
                return CheckpointCasOutcome.Rejected(current);
            }

            // A normal persist must target either a first-ever create (absent) or an ACTIVE row. Tombstoned rows are
            // only advanced by CommitRebuiltAsync.
            if (current.Exists && current.Lifecycle != CheckpointLifecycle.Active)
            {
                return CheckpointCasOutcome.Rejected(current);
            }

            generation = entry?.Generation ?? 0;
            nextRevision = entry is null ? 1 : entry.Revision + 1;
            if (WouldOverflow(nextRevision))
            {
                return CheckpointCasOutcome.Corrupt();
            }

            if (fault == WriteFaultMode.PreCommitFault)
            {
                // Rolled back BEFORE any durable change: the row is untouched, so the failure is known-safe to retry.
                return CheckpointCasOutcome.ProviderFailed(new IOException("injected pre-commit transport fault"));
            }

            // PostCommitResponseLossUnverifiable simulates a write whose commit did NOT take (but whose response was lost).
            if (fault != WriteFaultMode.PostCommitResponseLossUnverifiable)
            {
                _states[key] = payload.ToRecord();
                _stateData[key] = bytes;
                _control[key] = new ControlEntry(generation, nextRevision, CheckpointLifecycle.Active);
            }
            committedSlot = BuildSlotUnlocked(key);
        }

        // Post-commit response loss: the write's outcome is UNKNOWN to the caller — resolve it by a bounded, independent
        // re-read that confirms our exact resulting token + payload identity, else report InDoubt (typed retryable).
        if (fault is WriteFaultMode.PostCommitResponseLoss or WriteFaultMode.PostCommitResponseLossUnverifiable)
        {
            return await CheckpointInDoubtResolver.ResolveAsync(
                ct => ReadCheckpointSlotAsync(payload.ProjectorName, payload.ProjectorVersion, ct),
                CheckpointInDoubtResolver.CommittedByExactResult(
                    generation, nextRevision, CheckpointLifecycle.Active, payload.LastSortableUniqueId, payload.EventsProcessed),
                maxAttempts: 3,
                cause: new IOException("injected post-commit response loss"));
        }

        // Post-commit re-read unavailable: the write DID commit (the row advanced above), but the response was lost AND
        // every bounded independent re-read now throws (authority unreachable). Because the commit already happened, the
        // outcome must be InDoubt — proving the resolver never downgrades unreadable-after-commit to ProviderFailure.
        if (fault is WriteFaultMode.PostCommitRereadUnavailable)
        {
            return await CheckpointInDoubtResolver.ResolveAsync(
                _ => throw new IOException("injected re-read authority unreachable"),
                CheckpointInDoubtResolver.CommittedByExactResult(
                    generation, nextRevision, CheckpointLifecycle.Active, payload.LastSortableUniqueId, payload.EventsProcessed),
                maxAttempts: 3,
                cause: new IOException("injected post-commit response loss (unreadable authority)"));
        }

        return CheckpointCasOutcome.Committed(committedSlot);
    }

    public Task<CheckpointCasOutcome> InvalidateWithTombstoneAsync(
        string projectorName,
        string projectorVersion,
        CheckpointExpectation expectation,
        CancellationToken cancellationToken = default)
    {
        var key = (CurrentServiceId, projectorName, projectorVersion);
        lock (_casGate)
        {
            if (!ExpectationHoldsUnlocked(expectation, key, out var current, out var entry))
            {
                return Task.FromResult(CheckpointCasOutcome.Rejected(current));
            }
            if (!current.IsActive || entry is null)
            {
                // Only an Active row can be invalidated; an already-tombstoned row means another cluster won the bump.
                return Task.FromResult(CheckpointCasOutcome.Rejected(current));
            }
            if (WouldOverflow(entry.Generation) || WouldOverflow(entry.Revision + 1))
            {
                return Task.FromResult(CheckpointCasOutcome.Corrupt());
            }

            // Bump generation + revision, flip to Tombstoned. The prior payload/offload is RETAINED under the tombstone.
            _control[key] = new ControlEntry(entry.Generation + 1, entry.Revision + 1, CheckpointLifecycle.Tombstoned);
            return Task.FromResult(CheckpointCasOutcome.Committed(BuildSlotUnlocked(key)));
        }
    }

    public async Task<CheckpointCasOutcome> CommitRebuiltAsync(
        MultiProjectionStateWriteRequest payload,
        Stream stream,
        CheckpointExpectation expectation,
        int offloadThresholdBytes,
        CancellationToken cancellationToken = default)
    {
        var (_, bytes) = await BufferAsync(stream, cancellationToken).ConfigureAwait(false);
        var key = (CurrentServiceId, payload.ProjectorName, payload.ProjectorVersion);
        lock (_casGate)
        {
            if (!ExpectationHoldsUnlocked(expectation, key, out var current, out var entry))
            {
                return CheckpointCasOutcome.Rejected(current);
            }
            if (!current.IsTombstoned || entry is null)
            {
                return CheckpointCasOutcome.Rejected(current);
            }
            if (WouldOverflow(entry.Revision + 1))
            {
                return CheckpointCasOutcome.Corrupt();
            }

            // One atomic same-row CAS: write the rebuilt payload AND clear the tombstone (Tombstoned(g+1) -> Active(g+1)).
            _states[key] = payload.ToRecord();
            _stateData[key] = bytes;
            _control[key] = new ControlEntry(entry.Generation, entry.Revision + 1, CheckpointLifecycle.Active);
            return CheckpointCasOutcome.Committed(BuildSlotUnlocked(key));
        }
    }
}
