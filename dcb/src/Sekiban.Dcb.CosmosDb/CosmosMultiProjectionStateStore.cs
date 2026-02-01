using Microsoft.Azure.Cosmos;
using ResultBoxes;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Cosmos DB implementation of IMultiProjectionStateStore.
/// </summary>
public class CosmosMultiProjectionStateStore : IMultiProjectionStateStore
{
    private readonly CosmosDbContext _context;
    private readonly IBlobStorageSnapshotAccessor? _blobAccessor;
    private readonly IServiceIdProvider _serviceIdProvider;
    private readonly ICosmosContainerResolver _containerResolver;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CosmosMultiProjectionStateStore"/> class.
    /// </summary>
    /// <param name="context">The CosmosDB context.</param>
    /// <param name="serviceIdProvider">ServiceId provider for tenant isolation.</param>
    /// <param name="containerResolver">Container resolver for ServiceId routing.</param>
    /// <param name="blobAccessor">Optional blob storage accessor for offloaded state data.</param>
    public CosmosMultiProjectionStateStore(
        CosmosDbContext context,
        IServiceIdProvider serviceIdProvider,
        ICosmosContainerResolver containerResolver,
        IBlobStorageSnapshotAccessor? blobAccessor = null)
    {
        _context = context;
        _serviceIdProvider = serviceIdProvider ?? throw new ArgumentNullException(nameof(serviceIdProvider));
        _containerResolver = containerResolver ?? throw new ArgumentNullException(nameof(containerResolver));
        _blobAccessor = blobAccessor;
    }

    private string CurrentServiceId => _serviceIdProvider.GetCurrentServiceId();

    private static string GetPartitionKey(string partitionKey, CosmosContainerSettings settings, string serviceId) =>
        settings.IsLegacy ? partitionKey : $"{serviceId}|{partitionKey}";

    /// <inheritdoc />
    public async Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var serviceId = CurrentServiceId;
            var settings = _containerResolver.ResolveStatesContainer(serviceId);
            var container = await _context.GetMultiProjectionStatesContainerAsync(settings).ConfigureAwait(false);
            var partitionKey = $"MultiProjectionState_{projectorName}";
            var pk = GetPartitionKey(partitionKey, settings, serviceId);

            var response = await container.ReadItemAsync<CosmosMultiProjectionState>(
                projectorVersion,
                new PartitionKey(pk),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var doc = response.Resource;
            if (doc == null)
                return ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty);
            if (settings.IsLegacy)
            {
                if (!string.IsNullOrEmpty(doc.ServiceId) && !string.Equals(doc.ServiceId, serviceId, StringComparison.Ordinal))
                {
                    return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(
                        new UnauthorizedAccessException(
                            $"Projection state does not belong to service {serviceId}."));
                }
            }
            else if (!string.Equals(doc.ServiceId, serviceId, StringComparison.Ordinal))
            {
                return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(
                    new UnauthorizedAccessException(
                        $"Projection state does not belong to service {serviceId}."));
            }

            // Load from blob if offloaded - explicit error on failure
            byte[]? blobData = null;
            if (doc.IsOffloaded && _blobAccessor != null && doc.OffloadKey != null)
            {
                try
                {
                    blobData = await _blobAccessor.ReadAsync(doc.OffloadKey, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException blobEx)
                {
                    // Return explicit error instead of null stateData
                    return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(
                        new InvalidOperationException(
                            $"Failed to read offloaded state from blob storage. " +
                            $"Projector: {projectorName}, Version: {projectorVersion}, " +
                            $"BlobKey: {doc.OffloadKey}",
                            blobEx));
                }
                catch (System.IO.IOException blobEx)
                {
                    // Return explicit error instead of null stateData
                    return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(
                        new InvalidOperationException(
                            $"Failed to read offloaded state from blob storage (IO error). " +
                            $"Projector: {projectorName}, Version: {projectorVersion}, " +
                            $"BlobKey: {doc.OffloadKey}",
                            blobEx));
                }
            }

            return ResultBox.FromValue(OptionalValue.FromValue(doc.ToRecord(blobData)));
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty);
        }
    }

    /// <inheritdoc />
    public async Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(
        string projectorName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var serviceId = CurrentServiceId;
            var settings = _containerResolver.ResolveStatesContainer(serviceId);
            var container = await _context.GetMultiProjectionStatesContainerAsync(settings).ConfigureAwait(false);
            var partitionKey = $"MultiProjectionState_{projectorName}";
            var pk = GetPartitionKey(partitionKey, settings, serviceId);

            QueryDefinition query;
            if (settings.IsLegacy)
            {
                query = new QueryDefinition(
                        "SELECT * FROM c WHERE c.partitionKey = @pk ORDER BY c.eventsProcessed DESC")
                    .WithParameter("@pk", partitionKey);
            }
            else
            {
                query = new QueryDefinition(
                        "SELECT * FROM c WHERE c.pk = @pk ORDER BY c.eventsProcessed DESC")
                    .WithParameter("@pk", pk);
            }

            var iterator = container.GetItemQueryIterator<CosmosMultiProjectionState>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(pk),
                    MaxItemCount = 1
                });

            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
                var doc = response.FirstOrDefault();
                if (doc == null)
                    return ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty);
                if (settings.IsLegacy)
                {
                    if (!string.IsNullOrEmpty(doc.ServiceId) && !string.Equals(doc.ServiceId, serviceId, StringComparison.Ordinal))
                    {
                        return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(
                            new UnauthorizedAccessException(
                                $"Projection state does not belong to service {serviceId}."));
                    }
                }
                else if (!string.Equals(doc.ServiceId, serviceId, StringComparison.Ordinal))
                {
                    return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(
                        new UnauthorizedAccessException(
                            $"Projection state does not belong to service {serviceId}."));
                }

                // Load from blob if offloaded - explicit error on failure
                byte[]? blobData = null;
                if (doc.IsOffloaded && _blobAccessor != null && doc.OffloadKey != null)
                {
                    try
                    {
                        blobData = await _blobAccessor.ReadAsync(doc.OffloadKey, cancellationToken).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException blobEx)
                    {
                        // Return explicit error instead of null stateData
                        return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(
                            new InvalidOperationException(
                                $"Failed to read offloaded state from blob storage. " +
                                $"Projector: {projectorName}, " +
                                $"BlobKey: {doc.OffloadKey}",
                                blobEx));
                    }
                    catch (System.IO.IOException blobEx)
                    {
                        // Return explicit error instead of null stateData
                        return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(
                            new InvalidOperationException(
                                $"Failed to read offloaded state from blob storage (IO error). " +
                                $"Projector: {projectorName}, " +
                                $"BlobKey: {doc.OffloadKey}",
                                blobEx));
                    }
                }

                return ResultBox.FromValue(OptionalValue.FromValue(doc.ToRecord(blobData)));
            }

            return ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty);
        }
    }

    /// <inheritdoc />
    public async Task<ResultBox<bool>> UpsertAsync(
        MultiProjectionStateRecord record,
        int offloadThresholdBytes = 1_000_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        try
        {
            var serviceId = CurrentServiceId;
            var settings = _containerResolver.ResolveStatesContainer(serviceId);
            var container = await _context.GetMultiProjectionStatesContainerAsync(settings).ConfigureAwait(false);
            var partitionKey = record.GetPartitionKey();
            var pk = GetPartitionKey(partitionKey, settings, serviceId);

            var stateData = record.StateData;
            var isOffloaded = record.IsOffloaded;
            string? offloadKey = record.OffloadKey;
            string? offloadProvider = record.OffloadProvider;

            // Offload if compressed size exceeds threshold
            if (stateData != null &&
                stateData.Length > offloadThresholdBytes &&
                _blobAccessor != null)
            {
                offloadKey = await _blobAccessor.WriteAsync(
                    stateData,
                    $"{record.ProjectorName}/{record.ProjectorVersion}",
                    cancellationToken).ConfigureAwait(false);
                offloadProvider = _blobAccessor.ProviderName;
                isOffloaded = true;
                stateData = null;
            }

            var updatedRecord = record with
            {
                StateData = stateData,
                IsOffloaded = isOffloaded,
                OffloadKey = offloadKey,
                OffloadProvider = offloadProvider,
                UpdatedAt = DateTime.UtcNow
            };

            var doc = CosmosMultiProjectionState.FromRecord(updatedRecord, serviceId);

            await container.UpsertItemAsync(
                doc,
                new PartitionKey(pk),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return ResultBox.FromValue(true);
        }
        catch (CosmosException ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var serviceId = CurrentServiceId;
            var settings = _containerResolver.ResolveStatesContainer(serviceId);
            var container = await _context.GetMultiProjectionStatesContainerAsync(settings).ConfigureAwait(false);

            var query = settings.IsLegacy
                ? new QueryDefinition("SELECT * FROM c")
                : new QueryDefinition("SELECT * FROM c WHERE c.serviceId = @serviceId")
                    .WithParameter("@serviceId", serviceId);
            var iterator = container.GetItemQueryIterator<CosmosMultiProjectionState>(query);

            var results = new List<ProjectorStateInfo>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
                foreach (var doc in response)
                {
                    results.Add(new ProjectorStateInfo(
                        doc.ProjectorName,
                        doc.ProjectorVersion,
                        doc.EventsProcessed,
                        doc.UpdatedAt,
                        doc.OriginalSizeBytes,
                        doc.CompressedSizeBytes,
                        doc.LastSortableUniqueId));
                }
            }

            return ResultBox.FromValue<IReadOnlyList<ProjectorStateInfo>>(results);
        }
        catch (CosmosException ex)
        {
            return ResultBox.Error<IReadOnlyList<ProjectorStateInfo>>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<ResultBox<bool>> DeleteAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var serviceId = CurrentServiceId;
            var settings = _containerResolver.ResolveStatesContainer(serviceId);
            var container = await _context.GetMultiProjectionStatesContainerAsync(settings).ConfigureAwait(false);
            var partitionKey = $"MultiProjectionState_{projectorName}";
            var pk = GetPartitionKey(partitionKey, settings, serviceId);

            // Note: Offloaded blob cleanup should be handled separately
            // (IBlobStorageSnapshotAccessor does not currently support deletion)

            // Delete the document
            await container.DeleteItemAsync<CosmosMultiProjectionState>(
                projectorVersion,
                new PartitionKey(pk),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return ResultBox.FromValue(true);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ResultBox.FromValue(false);
        }
        catch (CosmosException ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<ResultBox<int>> DeleteAllAsync(
        string? projectorName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var serviceId = CurrentServiceId;
            var settings = _containerResolver.ResolveStatesContainer(serviceId);
            var container = await _context.GetMultiProjectionStatesContainerAsync(settings).ConfigureAwait(false);

            // Build query
            QueryDefinition query;
            if (!string.IsNullOrEmpty(projectorName))
            {
                var partitionKey = $"MultiProjectionState_{projectorName}";
                if (settings.IsLegacy)
                {
                    query = new QueryDefinition("SELECT * FROM c WHERE c.partitionKey = @pk")
                        .WithParameter("@pk", partitionKey);
                }
                else
                {
                    var pk = GetPartitionKey(partitionKey, settings, serviceId);
                    query = new QueryDefinition("SELECT * FROM c WHERE c.pk = @pk")
                        .WithParameter("@pk", pk);
                }
            }
            else
            {
                query = settings.IsLegacy
                    ? new QueryDefinition("SELECT * FROM c")
                    : new QueryDefinition("SELECT * FROM c WHERE c.serviceId = @serviceId")
                        .WithParameter("@serviceId", serviceId);
            }

            var iterator = container.GetItemQueryIterator<CosmosMultiProjectionState>(query);
            var deletedCount = 0;

            // Note: Offloaded blob cleanup should be handled separately
            // (IBlobStorageSnapshotAccessor does not currently support deletion)

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
                foreach (var doc in response)
                {
                    // Delete the document
                    await container.DeleteItemAsync<CosmosMultiProjectionState>(
                        doc.Id,
                        new PartitionKey(settings.IsLegacy ? doc.PartitionKey : doc.Pk),
                        cancellationToken: cancellationToken).ConfigureAwait(false);

                    deletedCount++;
                }
            }

            return ResultBox.FromValue(deletedCount);
        }
        catch (CosmosException ex)
        {
            return ResultBox.Error<int>(ex);
        }
    }
}
