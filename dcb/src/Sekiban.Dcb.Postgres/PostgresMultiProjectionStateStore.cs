using Microsoft.EntityFrameworkCore;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Postgres.DbModels;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.Postgres;

/// <summary>
///     Postgres implementation of IMultiProjectionStateStore.
/// </summary>
public class PostgresMultiProjectionStateStore : IMultiProjectionStateStore
{
    private readonly IDbContextFactory<SekibanDcbDbContext> _contextFactory;
    private readonly IBlobStorageSnapshotAccessor? _blobAccessor;
    private readonly IServiceIdProvider _serviceIdProvider;

    public PostgresMultiProjectionStateStore(
        IDbContextFactory<SekibanDcbDbContext> contextFactory,
        IServiceIdProvider serviceIdProvider,
        IBlobStorageSnapshotAccessor? blobAccessor = null)
    {
        _contextFactory = contextFactory;
        _serviceIdProvider = serviceIdProvider ?? throw new ArgumentNullException(nameof(serviceIdProvider));
        _blobAccessor = blobAccessor;
    }

    private string CurrentServiceId => _serviceIdProvider.GetCurrentServiceId();

    public async Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var serviceId = CurrentServiceId;
            var entity = await ctx.MultiProjectionStates
                .FirstOrDefaultAsync(s =>
                    s.ServiceId == serviceId &&
                    s.ProjectorName == projectorName &&
                    s.ProjectorVersion == projectorVersion, cancellationToken);

            if (entity == null)
                return ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty);

            // Load from blob if offloaded
            byte[]? stateData = entity.StateData;
            if (entity.IsOffloaded && _blobAccessor != null && entity.OffloadKey != null)
            {
                try
                {
                    stateData = await _blobAccessor.ReadAsync(entity.OffloadKey, cancellationToken);
                }
                catch (Exception ex)
                {
                    return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(
                        new InvalidOperationException(
                            $"Failed to read offloaded state from blob: {entity.OffloadKey}", ex));
                }

                if (stateData == null)
                {
                    return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(
                        new InvalidOperationException(
                            $"Offloaded state read returned null for key: {entity.OffloadKey}"));
                }
            }

            return ResultBox.FromValue(OptionalValue.FromValue(entity.ToRecord(stateData)));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(ex);
        }
    }

    public async Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(
        string projectorName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var serviceId = CurrentServiceId;
            var entity = await ctx.MultiProjectionStates
                .Where(s => s.ServiceId == serviceId && s.ProjectorName == projectorName)
                .OrderByDescending(s => s.EventsProcessed)
                .FirstOrDefaultAsync(cancellationToken);

            if (entity == null)
                return ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty);

            // Load from blob if offloaded
            byte[]? stateData = entity.StateData;
            if (entity.IsOffloaded && _blobAccessor != null && entity.OffloadKey != null)
            {
                try
                {
                    stateData = await _blobAccessor.ReadAsync(entity.OffloadKey, cancellationToken);
                }
                catch (Exception ex)
                {
                    return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(
                        new InvalidOperationException(
                            $"Failed to read offloaded state from blob: {entity.OffloadKey}", ex));
                }

                if (stateData == null)
                {
                    return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(
                        new InvalidOperationException(
                            $"Offloaded state read returned null for key: {entity.OffloadKey}"));
                }
            }

            return ResultBox.FromValue(OptionalValue.FromValue(entity.ToRecord(stateData)));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(ex);
        }
    }

    public async Task<ResultBox<bool>> UpsertAsync(
        MultiProjectionStateRecord record,
        int offloadThresholdBytes = 1_000_000,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var serviceId = CurrentServiceId;

            var offloadResult = await StreamOffloadHelper.ProcessAsync(
                record.StateData,
                $"{record.ProjectorName}/{record.ProjectorVersion}",
                offloadThresholdBytes,
                _blobAccessor,
                cancellationToken);

            var updatedRecord = record with
            {
                StateData = offloadResult.InlineData,
                IsOffloaded = offloadResult.IsOffloaded,
                OffloadKey = offloadResult.OffloadKey,
                OffloadProvider = offloadResult.OffloadProvider,
                UpdatedAt = DateTime.UtcNow
            };
            var dbRecord = DbMultiProjectionState.FromRecord(updatedRecord, serviceId);

            await UpsertDbRecordAsync(ctx, serviceId, record.ProjectorName, record.ProjectorVersion, dbRecord, cancellationToken);
            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    /// <summary>
    ///     Stream-based upsert with offload via StreamOffloadHelper.
    /// </summary>
    public async Task<ResultBox<bool>> UpsertFromStreamAsync(
        MultiProjectionStateWriteRequest request,
        Stream stream,
        int offloadThresholdBytes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var offloadResult = await StreamOffloadHelper.ProcessAsync(
                stream,
                $"{request.ProjectorName}/{request.ProjectorVersion}",
                offloadThresholdBytes,
                _blobAccessor,
                cancellationToken);

            var record = (request with
            {
                StateData = offloadResult.InlineData,
                IsOffloaded = offloadResult.IsOffloaded,
                OffloadKey = offloadResult.OffloadKey,
                OffloadProvider = offloadResult.OffloadProvider,
                UpdatedAt = DateTime.UtcNow
            }).ToRecord();

            await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var serviceId = CurrentServiceId;

            var dbRecord = DbMultiProjectionState.FromRecord(record, serviceId);

            await UpsertDbRecordAsync(ctx, serviceId, record.ProjectorName, record.ProjectorVersion, dbRecord, cancellationToken);
            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    private static async Task UpsertDbRecordAsync(
        SekibanDcbDbContext ctx,
        string serviceId,
        string projectorName,
        string projectorVersion,
        DbMultiProjectionState dbRecord,
        CancellationToken cancellationToken)
    {
        var existing = await ctx.MultiProjectionStates
            .FirstOrDefaultAsync(s =>
                s.ServiceId == serviceId &&
                s.ProjectorName == projectorName &&
                s.ProjectorVersion == projectorVersion, cancellationToken);

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

    public async Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var list = await ctx.MultiProjectionStates
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

            return ResultBox.FromValue<IReadOnlyList<ProjectorStateInfo>>(list);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IReadOnlyList<ProjectorStateInfo>>(ex);
        }
    }

    public async Task<ResultBox<bool>> DeleteAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var serviceId = CurrentServiceId;
            var entity = await ctx.MultiProjectionStates
                .FirstOrDefaultAsync(s =>
                    s.ServiceId == serviceId &&
                    s.ProjectorName == projectorName &&
                    s.ProjectorVersion == projectorVersion, cancellationToken);

            if (entity == null)
                return ResultBox.FromValue(false);

            // Note: Offloaded blob cleanup should be handled separately
            // (IBlobStorageSnapshotAccessor does not currently support deletion)

            ctx.MultiProjectionStates.Remove(entity);
            await ctx.SaveChangesAsync(cancellationToken);
            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    public async Task<ResultBox<int>> DeleteAllAsync(
        string? projectorName = null,
        CancellationToken cancellationToken = default)
    {
        try
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
            return ResultBox.FromValue(entities.Count);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<int>(ex);
        }
    }
}
