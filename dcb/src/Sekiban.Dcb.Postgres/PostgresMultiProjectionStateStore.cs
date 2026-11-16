using Microsoft.EntityFrameworkCore;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Postgres.DbModels;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Storage.Checkpoints;
using Sekiban.Dcb.Capabilities;

namespace Sekiban.Dcb.Postgres;

/// <summary>
///     Postgres implementation of IMultiProjectionStateStore. SEK-G20: also the generation/tombstone/exact-token CAS
///     surface (<see cref="IGenerationAwareCheckpointStore" />) — the AUTHORITATIVE provider — using conditional
///     row-count UPDATEs on the exact (Generation, Revision, Lifecycle) token.
/// </summary>
public partial class PostgresMultiProjectionStateStore :
    IMultiProjectionStateStore,
    IProjectionStatusStore,
    IStorageDurabilityDescriptorProvider,
    IGenerationAwareCheckpointStore
{
    /// <summary>Projection state lands in Postgres.</summary>
    public StorageDurabilityDescriptor DescribeStorage() =>
        new(StorageDurability.Durable, "Postgres");

    private readonly IDbContextFactory<SekibanDcbDbContext> _contextFactory;
    private readonly IBlobStorageSnapshotAccessor? _blobAccessor;
    private readonly IServiceIdProvider _serviceIdProvider;
    private readonly ProjectionStatusOptions _projectionStatusOptions;

    public PostgresMultiProjectionStateStore(
        IDbContextFactory<SekibanDcbDbContext> contextFactory,
        IServiceIdProvider serviceIdProvider,
        IBlobStorageSnapshotAccessor? blobAccessor = null)
        : this(contextFactory, serviceIdProvider, blobAccessor, new ProjectionStatusOptions())
    {
    }

    /// <summary>
    ///     Additive options-aware constructor. The historical constructor above remains the legacy auto-provisioning
    ///     default; pre-provisioned hosts pass the options explicitly without changing the public old ABI.
    /// </summary>
    public PostgresMultiProjectionStateStore(
        IDbContextFactory<SekibanDcbDbContext> contextFactory,
        IServiceIdProvider serviceIdProvider,
        IBlobStorageSnapshotAccessor? blobAccessor,
        ProjectionStatusOptions projectionStatusOptions)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _serviceIdProvider = serviceIdProvider ?? throw new ArgumentNullException(nameof(serviceIdProvider));
        _blobAccessor = blobAccessor;
        _projectionStatusOptions = projectionStatusOptions ?? throw new ArgumentNullException(nameof(projectionStatusOptions));
        _projectionStatusOptions.Validate();
    }

    private string CurrentServiceId => _serviceIdProvider.GetCurrentServiceId();

    public async Task<MultiProjectionStateRecord?> GetLatestForVersionAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await FindStateAsync(ctx, CurrentServiceId, projectorName, projectorVersion, cancellationToken);
        return entity?.ToRecord();
    }

    public async Task<MultiProjectionStateRecord?> GetLatestAnyVersionAsync(
        string projectorName,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var serviceId = CurrentServiceId;
        var entity = await ctx.MultiProjectionStates
            .Where(s => s.ServiceId == serviceId && s.ProjectorName == projectorName)
            .OrderByDescending(s => s.EventsProcessed)
            .FirstOrDefaultAsync(cancellationToken);
        return entity?.ToRecord();
    }

    public async Task<bool> UpsertAsync(
        MultiProjectionStateRecord record,
        int offloadThresholdBytes = 1_000_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!record.IsOffloaded)
        {
            throw new NotSupportedException(
                "UpsertAsync without payload stream is not supported for non-offloaded snapshots. Use UpsertFromStreamAsync.");
        }

        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var serviceId = CurrentServiceId;
        var dbRecord = DbMultiProjectionState.FromRecord(record with { UpdatedAt = DateTime.UtcNow }, serviceId, stateData: null);

        await UpsertDbRecordAsync(ctx, serviceId, record.ProjectorName, record.ProjectorVersion, dbRecord, cancellationToken);
        return true;
    }

    public async Task<Stream> OpenStateDataReadStreamAsync(
        MultiProjectionStateRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.IsOffloaded && _blobAccessor != null && !string.IsNullOrWhiteSpace(record.OffloadKey))
        {
            try
            {
                return await _blobAccessor.OpenReadAsync(record.OffloadKey, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                throw new InvalidOperationException(
                    $"Failed to open offloaded state stream: {record.OffloadKey}",
                    ex);
            }
        }

        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await FindStateAsync(ctx, CurrentServiceId, record.ProjectorName, record.ProjectorVersion, cancellationToken);

        if (entity?.StateData != null)
        {
            return new MemoryStream(entity.StateData, writable: false);
        }

        throw new InvalidOperationException(
            $"Projection state has no inline data and no readable offload stream: {record.ProjectorName}/{record.ProjectorVersion}");
    }

    private static Task<DbMultiProjectionState?> FindStateAsync(
        SekibanDcbDbContext ctx,
        string serviceId,
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken) =>
        ctx.MultiProjectionStates
            .FirstOrDefaultAsync(s =>
                s.ServiceId == serviceId &&
                s.ProjectorName == projectorName &&
                s.ProjectorVersion == projectorVersion, cancellationToken);

    /// <summary>
    ///     Stream-based upsert with offload via StreamOffloadHelper.
    /// </summary>
    public async Task<bool> UpsertFromStreamAsync(
        MultiProjectionStateWriteRequest request,
        Stream stream,
        int offloadThresholdBytes,
        CancellationToken cancellationToken = default)
    {
        var offloadResult = await StreamOffloadHelper.ProcessAsync(
            stream,
            $"{request.ProjectorName}/{request.ProjectorVersion}",
            offloadThresholdBytes,
            _blobAccessor,
            cancellationToken);
        var record = (request with
        {
            IsOffloaded = offloadResult.IsOffloaded,
            OffloadKey = offloadResult.OffloadKey,
            OffloadProvider = offloadResult.OffloadProvider,
            UpdatedAt = DateTime.UtcNow
        }).ToRecord();

        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var serviceId = CurrentServiceId;

        var dbRecord = DbMultiProjectionState.FromRecord(record, serviceId, offloadResult.InlineData);

        await UpsertDbRecordAsync(ctx, serviceId, record.ProjectorName, record.ProjectorVersion, dbRecord, cancellationToken);
        return true;
    }

    private static async Task UpsertDbRecordAsync(
        SekibanDcbDbContext ctx,
        string serviceId,
        string projectorName,
        string projectorVersion,
        DbMultiProjectionState dbRecord,
        CancellationToken cancellationToken)
    {
        var existing = await FindStateAsync(ctx, serviceId, projectorName, projectorVersion, cancellationToken);

        if (existing != null)
        {
            ctx.Entry(existing).CurrentValues.SetValues(dbRecord);
        }
        else
        {
            ctx.MultiProjectionStates.Add(dbRecord);
        }

        await ctx.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectorStateInfo>> ListAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await ctx.MultiProjectionStates
            .Where(s => s.ServiceId == CurrentServiceId)
            .Select(s => new ProjectorStateInfo(
                s.ProjectorName,
                s.ProjectorVersion,
                s.EventsProcessed,
                s.UpdatedAt,
                s.OriginalSizeBytes,
                s.CompressedSizeBytes,
                s.LastSortableUniqueId))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await FindStateAsync(ctx, CurrentServiceId, projectorName, projectorVersion, cancellationToken);

        if (entity == null)
            return false;

        // Note: Offloaded blob cleanup should be handled separately
        // (IBlobStorageSnapshotAccessor does not currently support deletion)

        ctx.MultiProjectionStates.Remove(entity);
        await ctx.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> DeleteAllAsync(
        string? projectorName = null,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<DbMultiProjectionState> query = ctx.MultiProjectionStates;
        if (!string.IsNullOrEmpty(projectorName))
        {
            query = query.Where(s => s.ServiceId == CurrentServiceId && s.ProjectorName == projectorName);
        }
        else
        {
            query = query.Where(s => s.ServiceId == CurrentServiceId);
        }

        var entities = await query.ToListAsync(cancellationToken);

        // Note: Offloaded blob cleanup should be handled separately
        // (IBlobStorageSnapshotAccessor does not currently support deletion)

        ctx.MultiProjectionStates.RemoveRange(entities);
        await ctx.SaveChangesAsync(cancellationToken);
        return entities.Count;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // SEK-G20 generation/tombstone/exact-token CAS (Postgres native, authoritative)
    // ---------------------------------------------------------------------------------------------------------------

    public CheckpointStoreCapabilityDescriptor DescribeCheckpointCapability() =>
        CheckpointStoreCapabilityDescriptor.Supporting("Postgres", CheckpointCapabilityKind.GenerationTombstoneCas);

    private static CheckpointSlot SlotFrom(DbMultiProjectionState e) => new(
        true, e.Generation, e.Revision.ToString(), (CheckpointLifecycle)e.Lifecycle, e.ToRecord());

    public async Task<CheckpointSlot> ReadCheckpointSlotAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var serviceId = CurrentServiceId;
        var entity = await ctx.MultiProjectionStates
            .AsNoTracking()
            .FirstOrDefaultAsync(s =>
                s.ServiceId == serviceId && s.ProjectorName == projectorName && s.ProjectorVersion == projectorVersion,
                cancellationToken);
        return entity is null ? CheckpointSlot.Absent : SlotFrom(entity);
    }

    /// <summary>True only for a Postgres unique-violation (23505) — the "row already exists" conflict on an insert.</summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation };

    /// <summary>
    ///     A DETERMINISTIC pre-commit failure — a syntax/schema error (SQL-state class 42, e.g. undefined_column when the
    ///     additive migration is unapplied) or an already-cancelled token (no request dispatched). These are provably NOT
    ///     post-commit, so they are ProviderFailure/fail-closed, never in-doubt.
    /// </summary>
    private static bool IsDeterministicPreCommitFailure(Exception ex, CancellationToken ct)
    {
        if (ct.IsCancellationRequested && ex is OperationCanceledException)
        {
            return true;
        }
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is Npgsql.PostgresException pg && pg.SqlState is { Length: >= 2 } s && s[0] == '4' && s[1] == '2')
            {
                return true; // class 42 = syntax error or access rule violation (undefined column/table/etc.)
            }
        }
        return false;
    }

    private async Task<CheckpointSlot> RefetchSlotAsync(string projectorName, string projectorVersion, CancellationToken ct)
    {
        try
        {
            return await ReadCheckpointSlotAsync(projectorName, projectorVersion, ct);
        }
        catch (Exception)
        {
            // An unreadable refetch reports the slot as absent (unchanged behaviour, cancellation included).
            return CheckpointSlot.Absent;
        }
    }

    public async Task<CheckpointCasOutcome> ConditionalUpsertAsync(
        MultiProjectionStateWriteRequest payload,
        Stream stream,
        CheckpointExpectation expectation,
        int offloadThresholdBytes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var offload = await StreamOffloadHelper.ProcessAsync(
                stream, $"{payload.ProjectorName}/{payload.ProjectorVersion}", offloadThresholdBytes, _blobAccessor, cancellationToken);
            var record = (payload with
            {
                IsOffloaded = offload.IsOffloaded,
                OffloadKey = offload.OffloadKey,
                OffloadProvider = offload.OffloadProvider,
                UpdatedAt = DateTime.UtcNow
            }).ToRecord();

            await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var serviceId = CurrentServiceId;

            if (expectation.ExpectAbsent)
            {
                // First-ever create = expected-absence CAS. The composite PK enforces atomicity; a concurrent create
                // raises a unique violation which we classify as ConditionRejected (refetch).
                var db = DbMultiProjectionState.FromRecord(record, serviceId, offload.InlineData);
                db.Generation = 0;
                db.Revision = 1;
                db.Lifecycle = (int)CheckpointLifecycle.Active;
                ctx.MultiProjectionStates.Add(db);
                try
                {
                    await ctx.SaveChangesAsync(cancellationToken);
                    return CheckpointCasOutcome.Committed(SlotFrom(db));
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    // Only a PK unique violation is a "row already exists" conflict (ConditionRejected). Any OTHER failure
                    // (e.g. an undefined column when the additive schema has not been applied) must FAIL CLOSED, never be
                    // laundered into a rejection — it propagates to the outer catch as a ProviderFailure.
                    return CheckpointCasOutcome.Rejected(
                        await RefetchSlotAsync(payload.ProjectorName, payload.ProjectorVersion, cancellationToken));
                }
            }

            if (!expectation.TryGetExactRevision(out var expectedRevision))
            {
                return CheckpointCasOutcome.Corrupt();
            }

            // Conditional UPDATE on the EXACT (generation, revision, Active) token — a single-round-trip atomic CAS.
            var affected = await ctx.MultiProjectionStates
                .Where(s => s.ServiceId == serviceId && s.ProjectorName == payload.ProjectorName
                    && s.ProjectorVersion == payload.ProjectorVersion
                    && s.Generation == expectation.ExpectedGeneration && s.Revision == expectedRevision
                    && s.Lifecycle == (int)CheckpointLifecycle.Active)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.PayloadType, record.PayloadType)
                    .SetProperty(s => s.LastSortableUniqueId, record.LastSortableUniqueId)
                    .SetProperty(s => s.EventsProcessed, record.EventsProcessed)
                    .SetProperty(s => s.StateData, offload.InlineData)
                    .SetProperty(s => s.IsOffloaded, record.IsOffloaded)
                    .SetProperty(s => s.OffloadKey, record.OffloadKey)
                    .SetProperty(s => s.OffloadProvider, record.OffloadProvider)
                    .SetProperty(s => s.OriginalSizeBytes, record.OriginalSizeBytes)
                    .SetProperty(s => s.CompressedSizeBytes, record.CompressedSizeBytes)
                    .SetProperty(s => s.SafeWindowThreshold, record.SafeWindowThreshold)
                    .SetProperty(s => s.UpdatedAt, record.UpdatedAt)
                    .SetProperty(s => s.BuildSource, record.BuildSource)
                    .SetProperty(s => s.BuildHost, record.BuildHost)
                    .SetProperty(s => s.Revision, s => s.Revision + 1), cancellationToken);

            var slot = await RefetchSlotAsync(payload.ProjectorName, payload.ProjectorVersion, cancellationToken);
            return affected == 1 ? CheckpointCasOutcome.Committed(slot) : CheckpointCasOutcome.Rejected(slot);
        }
        catch (Exception ex)
        {
            // SEK-G20: a dispatch/transport failure whose commit is UNKNOWN — resolve via a bounded independent re-read.
            return await CheckpointInDoubtResolver.ClassifyActiveWriteFailure(
                IsDeterministicPreCommitFailure(ex, cancellationToken), ex,
                ct => ReadCheckpointSlotAsync(payload.ProjectorName, payload.ProjectorVersion, ct),
                expectation.ExpectAbsent ? 0 : expectation.ExpectedGeneration, payload.LastSortableUniqueId, payload.EventsProcessed);
        }
    }

    public async Task<CheckpointCasOutcome> InvalidateWithTombstoneAsync(
        string projectorName,
        string projectorVersion,
        CheckpointExpectation expectation,
        CancellationToken cancellationToken = default)
    {
        if (!expectation.TryGetExactRevision(out var expectedRevision))
        {
            return CheckpointCasOutcome.Corrupt();
        }
        try
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var serviceId = CurrentServiceId;

            // Bump generation + revision, flip to Tombstoned, on the exact Active token. Prior payload/offload retained.
            var affected = await ctx.MultiProjectionStates
                .Where(s => s.ServiceId == serviceId && s.ProjectorName == projectorName
                    && s.ProjectorVersion == projectorVersion
                    && s.Generation == expectation.ExpectedGeneration && s.Revision == expectedRevision
                    && s.Lifecycle == (int)CheckpointLifecycle.Active)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.Generation, s => s.Generation + 1)
                    .SetProperty(s => s.Revision, s => s.Revision + 1)
                    .SetProperty(s => s.Lifecycle, (int)CheckpointLifecycle.Tombstoned)
                    .SetProperty(s => s.UpdatedAt, DateTime.UtcNow), cancellationToken);

            var slot = await RefetchSlotAsync(projectorName, projectorVersion, cancellationToken);
            return affected == 1 ? CheckpointCasOutcome.Committed(slot) : CheckpointCasOutcome.Rejected(slot);
        }
        catch (Exception ex)
        {
            // SEK-G20: a lost response on the tombstone UPDATE is UNKNOWN-commit too — a deterministic pre-commit/schema
            // failure is ProviderFailure, but any other failure crossed a commit-capable boundary and MUST be resolved by
            // a bounded independent re-read (Tombstoned at g+1 => our own commit; unconfirmable => typed retryable InDoubt).
            return await CheckpointInDoubtResolver.ClassifyTombstoneWriteFailure(
                IsDeterministicPreCommitFailure(ex, cancellationToken), ex,
                ct => ReadCheckpointSlotAsync(projectorName, projectorVersion, ct),expectation.ExpectedGeneration + 1, expectedRevision + 1);
        }
    }

    public async Task<CheckpointCasOutcome> CommitRebuiltAsync(
        MultiProjectionStateWriteRequest payload,
        Stream stream,
        CheckpointExpectation expectation,
        int offloadThresholdBytes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!expectation.TryGetExactRevision(out var expectedRevision))
            {
                return CheckpointCasOutcome.Corrupt();
            }
            var offload = await StreamOffloadHelper.ProcessAsync(
                stream, $"{payload.ProjectorName}/{payload.ProjectorVersion}", offloadThresholdBytes, _blobAccessor, cancellationToken);
            var record = (payload with
            {
                IsOffloaded = offload.IsOffloaded,
                OffloadKey = offload.OffloadKey,
                OffloadProvider = offload.OffloadProvider,
                UpdatedAt = DateTime.UtcNow
            }).ToRecord();

            await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var serviceId = CurrentServiceId;

            // One atomic same-row CAS on the exact Tombstoned token: write rebuilt payload AND clear the tombstone.
            var affected = await ctx.MultiProjectionStates
                .Where(s => s.ServiceId == serviceId && s.ProjectorName == payload.ProjectorName
                    && s.ProjectorVersion == payload.ProjectorVersion
                    && s.Generation == expectation.ExpectedGeneration && s.Revision == expectedRevision
                    && s.Lifecycle == (int)CheckpointLifecycle.Tombstoned)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.PayloadType, record.PayloadType)
                    .SetProperty(s => s.LastSortableUniqueId, record.LastSortableUniqueId)
                    .SetProperty(s => s.EventsProcessed, record.EventsProcessed)
                    .SetProperty(s => s.StateData, offload.InlineData)
                    .SetProperty(s => s.IsOffloaded, record.IsOffloaded)
                    .SetProperty(s => s.OffloadKey, record.OffloadKey)
                    .SetProperty(s => s.OffloadProvider, record.OffloadProvider)
                    .SetProperty(s => s.OriginalSizeBytes, record.OriginalSizeBytes)
                    .SetProperty(s => s.CompressedSizeBytes, record.CompressedSizeBytes)
                    .SetProperty(s => s.SafeWindowThreshold, record.SafeWindowThreshold)
                    .SetProperty(s => s.UpdatedAt, record.UpdatedAt)
                    .SetProperty(s => s.BuildSource, record.BuildSource)
                    .SetProperty(s => s.BuildHost, record.BuildHost)
                    .SetProperty(s => s.Lifecycle, (int)CheckpointLifecycle.Active)
                    .SetProperty(s => s.Revision, s => s.Revision + 1), cancellationToken);

            var slot = await RefetchSlotAsync(payload.ProjectorName, payload.ProjectorVersion, cancellationToken);
            return affected == 1 ? CheckpointCasOutcome.Committed(slot) : CheckpointCasOutcome.Rejected(slot);
        }
        catch (Exception ex)
        {
            // SEK-G20: a dispatch/transport failure whose commit is UNKNOWN — resolve via a bounded independent re-read.
            return await CheckpointInDoubtResolver.ClassifyActiveWriteFailure(
                IsDeterministicPreCommitFailure(ex, cancellationToken), ex,
                ct => ReadCheckpointSlotAsync(payload.ProjectorName, payload.ProjectorVersion, ct),
                expectation.ExpectedGeneration, payload.LastSortableUniqueId, payload.EventsProcessed);
        }
    }
}
