using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Storage.Checkpoints;

/// <summary>
///     Outcome of a checkpoint persist: saved, or not saved with the reason. A rejected or failed CAS is an expected
///     outcome the caller branches on (it takes the not-saved branch), so it is a value rather than an exception.
/// </summary>
internal readonly record struct CheckpointPersistOutcome(Exception? Failure)
{
    public bool Saved => Failure is null;

    public static CheckpointPersistOutcome Committed => default;

    public static CheckpointPersistOutcome NotSaved(Exception failure) => new(failure);
}

/// <summary>
///     SEK-G20 SOLE checkpoint-mutation coordinator. This is the ONE production type that holds the generation-aware CAS
///     surface (<see cref="IGenerationAwareCheckpointStore" />) and the ONLY type that calls ANY mutation — the CAS
///     transitions (ConditionalUpsert / InvalidateWithTombstone / CommitRebuilt) or the legacy fallbacks
///     (UpsertFromStream / Delete) — on the external checkpoint store. The Orleans grain, the CLI
///     <c>MultiProjectionStateBuilder</c>, and the admin-delete path call the SEMANTIC methods here and never touch a raw
///     mutation interface, so a stale writer cannot re-contaminate the shared row and the no-bypass IL guard has an exact,
///     single owner. A per-activation instance owns the adopted-token + rebuilt-pending state and arms the caller's query
///     barrier through the injected callback.
/// </summary>
internal sealed class CheckpointMutationCoordinator
{
    private readonly IMultiProjectionStateStore _store;
    private readonly IGenerationAwareCheckpointStore? _cas;
    private readonly Action _armQueryBarrier;

    public CheckpointMutationCoordinator(IMultiProjectionStateStore store, Action armQueryBarrier)
    {
        _store = store;
        _cas = CheckpointCapabilityResolver.SupportsGenerationCas(store)
            ? store as IGenerationAwareCheckpointStore
            : null;
        _armQueryBarrier = armQueryBarrier;
    }

    /// <summary>Whether the underlying store advertises the generation/tombstone CAS capability.</summary>
    public bool IsCapable => _cas is not null;

    /// <summary>The exact control-plane token this activation currently CASes against (null = expected-absence create).</summary>
    public CheckpointSlot? AdoptedSlot { get; private set; }

    /// <summary>True when this activation rebuilt over a tombstone and must CommitRebuilt on the exact tombstone token.</summary>
    public bool PendingRebuiltCommit { get; private set; }

    /// <summary>Reads the control-plane slot (capable stores only). Callers gate on this BEFORE binding any payload.</summary>
    public Task<CheckpointSlot> ReadSlotAsync(string projectorName, string projectorVersion, CancellationToken ct = default) =>
        _cas!.ReadCheckpointSlotAsync(projectorName, projectorVersion, ct);

    /// <summary>Adopts an ACTIVE slot on restore so the first persist CASes on its exact token (stale writers rejected).</summary>
    public void AdoptActive(CheckpointSlot slot)
    {
        AdoptedSlot = slot;
        PendingRebuiltCommit = false;
    }

    /// <summary>Adopts an observed TOMBSTONE on restore, flipping this activation into rebuilt-commit mode.</summary>
    public void AdoptTombstone(CheckpointSlot slot)
    {
        AdoptedSlot = slot;
        PendingRebuiltCommit = true;
    }

    /// <summary>
    ///     The stateful product persist (grain). On a capable store this is an EXPECTED-TOKEN CAS — a stale writer is
    ///     ConditionRejected and NEVER re-contaminates the row; a rebuilt commit is one atomic same-row CAS on the exact
    ///     tombstone token; a rejection refetches + adopts and, on a tombstone, arms the query barrier. Non-capable stores
    ///     keep the legacy unconditional upsert. Returns a not-saved outcome on a rejected/failed CAS so the caller takes the
    ///     not-saved branch. MUST run inside the caller's external-store serialisation gate.
    /// </summary>
    public async Task<CheckpointPersistOutcome> PersistAsync(MultiProjectionStateWriteRequest request, Stream stream, int offloadThreshold)
    {
        if (_cas is null)
        {
            // LEGACY-FALLBACK (non-capable): unconditional upsert (byte-for-byte).
            return await LegacyUpsertAsync(request, stream, offloadThreshold, CancellationToken.None);
        }

        // The adopted slot is per (projectorName, projectorVersion). A request that targets a DIFFERENT version (a version
        // rewrite) is not governed by the adopted token: read that version's current slot fresh and CAS on it (Absent =>
        // a first-ever create). The adopted-token anti-recontamination applies only to same-version persists.
        var adoptedMatchesRequest = AdoptedSlot?.Record is { } r
            && string.Equals(r.ProjectorName, request.ProjectorName, StringComparison.Ordinal)
            && string.Equals(r.ProjectorVersion, request.ProjectorVersion, StringComparison.Ordinal);

        if (!adoptedMatchesRequest)
        {
            var freshSlot = await ReadSlotOrNullAsync(
                _cas.ReadCheckpointSlotAsync(request.ProjectorName, request.ProjectorVersion, CancellationToken.None));
            var freshExpectation = freshSlot is { IsActive: true } freshActive
                ? CheckpointExpectation.FromSlot(freshActive)
                : CheckpointExpectation.Absent;
            var freshOutcome = await _cas.ConditionalUpsertAsync(request, stream, freshExpectation, offloadThreshold, CancellationToken.None);
            if (freshOutcome.Status == CheckpointCasStatus.Committed)
            {
                AdoptedSlot = freshOutcome.ResultingSlot;
                return CheckpointPersistOutcome.Committed;
            }
            return CheckpointPersistOutcome.NotSaved(freshOutcome.Cause ?? new InvalidOperationException(
                $"Checkpoint CAS for version {request.ProjectorVersion} was {freshOutcome.Status}."));
        }

        // Rebuilt commit: this activation rebuilt over a tombstone — commit on the exact tombstone token (one atomic CAS
        // that also clears the tombstone).
        if (PendingRebuiltCommit && AdoptedSlot is { IsTombstoned: true } tombstone)
        {
            var commit = await _cas.CommitRebuiltAsync(request, stream, CheckpointExpectation.FromSlot(tombstone), offloadThreshold, CancellationToken.None);
            switch (commit.Status)
            {
                case CheckpointCasStatus.Committed:
                    AdoptedSlot = commit.ResultingSlot;
                    PendingRebuiltCommit = false;
                    return CheckpointPersistOutcome.Committed;
                case CheckpointCasStatus.ConditionRejected:
                    // A peer rebuilt first (row is now Active at the bumped generation) OR re-tombstoned. Adopt the
                    // current slot; if it is Active a peer's rebuilt commit is authoritative and ours is unnecessary.
                    AdoptAfterRejection(commit.CurrentSlot);
                    return CheckpointPersistOutcome.NotSaved(new InvalidOperationException(
                        "Rebuilt-commit CAS was rejected (a peer moved the checkpoint); refetched and will re-evaluate."));
                default:
                    return CheckpointPersistOutcome.NotSaved(commit.Cause ?? new InvalidOperationException("Rebuilt-commit CAS failed."));
            }
        }

        // Normal persist: CAS on the adopted Active token, or a first-ever expected-absence create.
        var expectation = AdoptedSlot is { IsActive: true } active
            ? CheckpointExpectation.FromSlot(active)
            : CheckpointExpectation.Absent;
        var outcome = await _cas.ConditionalUpsertAsync(request, stream, expectation, offloadThreshold, CancellationToken.None);
        switch (outcome.Status)
        {
            case CheckpointCasStatus.Committed:
                AdoptedSlot = outcome.ResultingSlot;
                return CheckpointPersistOutcome.Committed;
            case CheckpointCasStatus.ConditionRejected:
                AdoptAfterRejection(outcome.CurrentSlot);
                return CheckpointPersistOutcome.NotSaved(new InvalidOperationException(
                    "Checkpoint CAS was rejected (a peer moved the shared row); refetched — no stale write applied."));
            default:
                return CheckpointPersistOutcome.NotSaved(outcome.Cause ?? new InvalidOperationException("Checkpoint CAS failed."));
        }
    }

    /// <summary>
    ///     The capability-aware invalidation shared by the retrograde-rebuild path and the admin delete path. On a capable
    ///     store it performs a durable bump+tombstone CAS (visible to other clusters before the local rebuild); non-capable
    ///     stores keep the legacy delete byte-for-byte. There is NO direct DeleteAsync bypass anywhere else in a product
    ///     mutation path. MUST run inside the caller's external-store serialisation gate.
    /// </summary>
    public async Task InvalidateAsync(
        string projectorName, string projectorVersion, CancellationToken cancellationToken = default)
    {
        if (_cas is not null)
        {
            // The read's own failure is captured (not thrown) so that only the caller's cancellation of the wait
            // propagates as itself; a failed read is reported below after that cancellation check.
            var slotRead = await CaptureSlotReadAsync(
                    _cas.ReadCheckpointSlotAsync(projectorName, projectorVersion, cancellationToken))
                .WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (slotRead.Failure is { } readFailure)
            {
                throw new InvalidOperationException(
                    $"Failed to read the checkpoint slot for tombstone invalidation: {readFailure.Message}",
                    readFailure);
            }
            var slot = slotRead.Slot!;
            if (slot.IsActive)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outcome = await _cas.InvalidateWithTombstoneAsync(
                    projectorName, projectorVersion, CheckpointExpectation.FromSlot(slot), cancellationToken);
                var resulting = outcome.ResultingSlot ?? outcome.CurrentSlot;
                AdoptedSlot = resulting ?? slot;
                PendingRebuiltCommit = true;
                if (outcome.Status is CheckpointCasStatus.ProviderFailure or CheckpointCasStatus.Corruption)
                {
                    throw new InvalidOperationException(
                        "Failed to durably bump+tombstone the checkpoint for a full rebuild.", outcome.Cause);
                }
            }
            else
            {
                AdoptedSlot = slot.Exists ? slot : null;
                PendingRebuiltCommit = slot.IsTombstoned;
            }
            return;
        }

        // Non-capable store: legacy delete-based invalidation (byte-for-byte unchanged). LEGACY-FALLBACK (non-capable).
        cancellationToken.ThrowIfCancellationRequested();
        var delete = _store.DeleteAsync(projectorName, projectorVersion, cancellationToken);
        try
        {
            await delete;
        }
        catch (Exception deleteFailure)
        {
            throw new InvalidOperationException(
                $"Failed to invalidate the external derived snapshot for a full rebuild: {deleteFailure.Message}",
                deleteFailure);
        }
    }

    /// <summary>
    ///     The STATELESS one-shot persist for the CLI/offline build (<c>MultiProjectionStateBuilder</c>): it reads the
    ///     current slot fresh and persists through the CAS (tombstone -> rebuilt commit; active -> exact-token CAS; absent
    ///     -> expected-absence create) so it cannot re-contaminate a shared row outside the tombstone protocol. Non-capable
    ///     stores keep the legacy unconditional upsert.
    /// </summary>
    public async Task<CheckpointPersistOutcome> PersistOnceAsync(
        MultiProjectionStateWriteRequest request, Stream stream, int offloadThreshold, CancellationToken ct)
    {
        if (_cas is null)
        {
            // LEGACY-FALLBACK (non-capable): unconditional upsert (byte-for-byte).
            return await LegacyUpsertAsync(request, stream, offloadThreshold, ct);
        }

        var slot = await ReadSlotOrNullAsync(_cas.ReadCheckpointSlotAsync(request.ProjectorName, request.ProjectorVersion, ct))
            ?? CheckpointSlot.Absent;
        var outcome = slot.IsTombstoned
            ? await _cas.CommitRebuiltAsync(request, stream, CheckpointExpectation.FromSlot(slot), offloadThreshold, ct)
            : await _cas.ConditionalUpsertAsync(
                request, stream,
                slot.IsActive ? CheckpointExpectation.FromSlot(slot) : CheckpointExpectation.Absent,
                offloadThreshold, ct);
        return outcome.Status == CheckpointCasStatus.Committed
            ? CheckpointPersistOutcome.Committed
            : CheckpointPersistOutcome.NotSaved(outcome.Cause ?? new InvalidOperationException(
                $"CLI checkpoint build CAS was {outcome.Status} for {request.ProjectorName}/{request.ProjectorVersion}."));
    }

    // LEGACY-FALLBACK (non-capable): a failed unconditional upsert is a not-saved outcome, as it always was.
    private async Task<CheckpointPersistOutcome> LegacyUpsertAsync(
        MultiProjectionStateWriteRequest request, Stream stream, int offloadThreshold, CancellationToken ct)
    {
        var upsert = _store.UpsertFromStreamAsync(request, stream, offloadThreshold, ct);
        try
        {
            await upsert;
            return CheckpointPersistOutcome.Committed;
        }
        catch (Exception ex)
        {
            return CheckpointPersistOutcome.NotSaved(ex);
        }
    }

    // A failed slot read leaves the slot unknown (null); the persist then proceeds as for an absent slot.
    private static async Task<CheckpointSlot?> ReadSlotOrNullAsync(Task<CheckpointSlot> read)
    {
        try
        {
            return await read;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<(CheckpointSlot? Slot, Exception? Failure)> CaptureSlotReadAsync(Task<CheckpointSlot> read)
    {
        try
        {
            return (await read, null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    // After a rejected CAS, adopt the freshly-observed slot. A TOMBSTONE means a peer began a retrograde rebuild: flip to
    // rebuilt-commit mode and arm the caller's query barrier so the next query replays the authoritative history from the
    // beginning (never serving the stale local state), then commits the rebuilt checkpoint on the exact tombstone token.
    private void AdoptAfterRejection(CheckpointSlot? current)
    {
        AdoptedSlot = current is { Exists: true } ? current : null;
        if (current is { IsTombstoned: true })
        {
            PendingRebuiltCommit = true;
            _armQueryBarrier();
        }
        else
        {
            PendingRebuiltCommit = false;
        }
    }
}
