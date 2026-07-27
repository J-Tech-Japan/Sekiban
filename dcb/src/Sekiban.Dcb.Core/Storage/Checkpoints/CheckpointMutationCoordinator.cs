using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Storage.Checkpoints;

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
    public Task<ResultBox<CheckpointSlot>> ReadSlotAsync(string projectorName, string projectorVersion, CancellationToken ct = default) =>
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
    ///     keep the legacy unconditional upsert. Returns Error on a rejected/failed CAS so the caller takes the not-saved
    ///     branch. MUST run inside the caller's external-store serialisation gate.
    /// </summary>
    public async Task<ResultBox<bool>> PersistAsync(MultiProjectionStateWriteRequest request, Stream stream, int offloadThreshold)
    {
        if (_cas is null)
        {
            // LEGACY-FALLBACK (non-capable): unconditional upsert (byte-for-byte).
            return await _store.UpsertFromStreamAsync(request, stream, offloadThreshold, CancellationToken.None);
        }

        // The adopted slot is per (projectorName, projectorVersion). A request that targets a DIFFERENT version (a version
        // rewrite) is not governed by the adopted token: read that version's current slot fresh and CAS on it (Absent =>
        // a first-ever create). The adopted-token anti-recontamination applies only to same-version persists.
        var adoptedMatchesRequest = AdoptedSlot?.Record is { } r
            && string.Equals(r.ProjectorName, request.ProjectorName, StringComparison.Ordinal)
            && string.Equals(r.ProjectorVersion, request.ProjectorVersion, StringComparison.Ordinal);

        if (!adoptedMatchesRequest)
        {
            var freshResult = await _cas.ReadCheckpointSlotAsync(request.ProjectorName, request.ProjectorVersion, CancellationToken.None);
            var freshExpectation = freshResult.IsSuccess && freshResult.GetValue() is { IsActive: true } freshActive
                ? CheckpointExpectation.FromSlot(freshActive)
                : CheckpointExpectation.Absent;
            var freshOutcome = await _cas.ConditionalUpsertAsync(request, stream, freshExpectation, offloadThreshold, CancellationToken.None);
            if (freshOutcome.Status == CheckpointCasStatus.Committed)
            {
                AdoptedSlot = freshOutcome.ResultingSlot;
                return ResultBox.FromValue(true);
            }
            return ResultBox.Error<bool>(freshOutcome.Cause ?? new InvalidOperationException(
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
                    return ResultBox.FromValue(true);
                case CheckpointCasStatus.ConditionRejected:
                    // A peer rebuilt first (row is now Active at the bumped generation) OR re-tombstoned. Adopt the
                    // current slot; if it is Active a peer's rebuilt commit is authoritative and ours is unnecessary.
                    AdoptAfterRejection(commit.CurrentSlot);
                    return ResultBox.Error<bool>(new InvalidOperationException(
                        "Rebuilt-commit CAS was rejected (a peer moved the checkpoint); refetched and will re-evaluate."));
                default:
                    return ResultBox.Error<bool>(commit.Cause ?? new InvalidOperationException("Rebuilt-commit CAS failed."));
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
                return ResultBox.FromValue(true);
            case CheckpointCasStatus.ConditionRejected:
                AdoptAfterRejection(outcome.CurrentSlot);
                return ResultBox.Error<bool>(new InvalidOperationException(
                    "Checkpoint CAS was rejected (a peer moved the shared row); refetched — no stale write applied."));
            default:
                return ResultBox.Error<bool>(outcome.Cause ?? new InvalidOperationException("Checkpoint CAS failed."));
        }
    }

    /// <summary>
    ///     The capability-aware invalidation shared by the retrograde-rebuild path and the admin delete path. On a capable
    ///     store it performs a durable bump+tombstone CAS (visible to other clusters before the local rebuild); non-capable
    ///     stores keep the legacy delete byte-for-byte. There is NO direct DeleteAsync bypass anywhere else in a product
    ///     mutation path. MUST run inside the caller's external-store serialisation gate.
    /// </summary>
    public async Task InvalidateAsync(string projectorName, string projectorVersion)
    {
        if (_cas is not null)
        {
            var slotResult = await _cas.ReadCheckpointSlotAsync(projectorName, projectorVersion);
            if (!slotResult.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Failed to read the checkpoint slot for tombstone invalidation: {slotResult.GetException().Message}",
                    slotResult.GetException());
            }
            var slot = slotResult.GetValue();
            if (slot.IsActive)
            {
                var outcome = await _cas.InvalidateWithTombstoneAsync(
                    projectorName, projectorVersion, CheckpointExpectation.FromSlot(slot));
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
        var deleteResult = await _store.DeleteAsync(projectorName, projectorVersion);
        if (!deleteResult.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to invalidate the external derived snapshot for a full rebuild: {deleteResult.GetException().Message}",
                deleteResult.GetException());
        }
    }

    /// <summary>
    ///     The STATELESS one-shot persist for the CLI/offline build (<c>MultiProjectionStateBuilder</c>): it reads the
    ///     current slot fresh and persists through the CAS (tombstone -> rebuilt commit; active -> exact-token CAS; absent
    ///     -> expected-absence create) so it cannot re-contaminate a shared row outside the tombstone protocol. Non-capable
    ///     stores keep the legacy unconditional upsert.
    /// </summary>
    public async Task<ResultBox<bool>> PersistOnceAsync(
        MultiProjectionStateWriteRequest request, Stream stream, int offloadThreshold, CancellationToken ct)
    {
        if (_cas is null)
        {
            // LEGACY-FALLBACK (non-capable): unconditional upsert (byte-for-byte).
            return await _store.UpsertFromStreamAsync(request, stream, offloadThreshold, ct);
        }

        var slotResult = await _cas.ReadCheckpointSlotAsync(request.ProjectorName, request.ProjectorVersion, ct);
        var slot = slotResult.IsSuccess ? slotResult.GetValue() : CheckpointSlot.Absent;
        var outcome = slot.IsTombstoned
            ? await _cas.CommitRebuiltAsync(request, stream, CheckpointExpectation.FromSlot(slot), offloadThreshold, ct)
            : await _cas.ConditionalUpsertAsync(
                request, stream,
                slot.IsActive ? CheckpointExpectation.FromSlot(slot) : CheckpointExpectation.Absent,
                offloadThreshold, ct);
        return outcome.Status == CheckpointCasStatus.Committed
            ? ResultBox.FromValue(true)
            : ResultBox.Error<bool>(outcome.Cause ?? new InvalidOperationException(
                $"CLI checkpoint build CAS was {outcome.Status} for {request.ProjectorName}/{request.ProjectorVersion}."));
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
