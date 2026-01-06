using Microsoft.EntityFrameworkCore;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Postgres.DbModels;
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

    public PostgresMultiProjectionStateStore(
        IDbContextFactory<SekibanDcbDbContext> contextFactory,
        IBlobStorageSnapshotAccessor? blobAccessor = null)
    {
        _contextFactory = contextFactory;
        _blobAccessor = blobAccessor;
    }

    public async Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await ctx.MultiProjectionStates
                .FirstOrDefaultAsync(s =>
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
            var entity = await ctx.MultiProjectionStates
                .Where(s => s.ProjectorName == projectorName)
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

            var stateData = record.StateData;
            var isOffloaded = record.IsOffloaded;
            string? offloadKey = record.OffloadKey;
            string? offloadProvider = record.OffloadProvider;

            // Offload if compressed size exceeds threshold
            if (stateData != null &&
                stateData.Length > offloadThresholdBytes &&
                _blobAccessor != null)
            {
                // Use projectorName/projectorVersion as path
                offloadKey = await _blobAccessor.WriteAsync(
                    stateData,
                    $"{record.ProjectorName}/{record.ProjectorVersion}",
                    cancellationToken);
                offloadProvider = _blobAccessor.ProviderName;
                isOffloaded = true;
                stateData = null;
            }

            var dbRecord = new DbMultiProjectionState
            {
                ProjectorName = record.ProjectorName,
                ProjectorVersion = record.ProjectorVersion,
                PayloadType = record.PayloadType,
                LastSortableUniqueId = record.LastSortableUniqueId,
                EventsProcessed = record.EventsProcessed,
                StateData = stateData,
                IsOffloaded = isOffloaded,
                OffloadKey = offloadKey,
                OffloadProvider = offloadProvider,
                OriginalSizeBytes = record.OriginalSizeBytes,
                CompressedSizeBytes = record.CompressedSizeBytes,
                SafeWindowThreshold = record.SafeWindowThreshold,
                CreatedAt = record.CreatedAt,
                UpdatedAt = DateTime.UtcNow,
                BuildSource = record.BuildSource,
                BuildHost = record.BuildHost
            };

            // Upsert
            var existing = await ctx.MultiProjectionStates
                .FirstOrDefaultAsync(s =>
                    s.ProjectorName == record.ProjectorName &&
                    s.ProjectorVersion == record.ProjectorVersion, cancellationToken);

            if (existing != null)
            {
                ctx.Entry(existing).CurrentValues.SetValues(dbRecord);
            }
            else
            {
                ctx.MultiProjectionStates.Add(dbRecord);
            }

            await ctx.SaveChangesAsync(cancellationToken);
            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    public async Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var list = await ctx.MultiProjectionStates
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
            var entity = await ctx.MultiProjectionStates
                .FirstOrDefaultAsync(s =>
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
                query = query.Where(s => s.ProjectorName == projectorName);
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
