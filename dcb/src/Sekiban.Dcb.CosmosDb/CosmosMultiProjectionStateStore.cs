using Microsoft.Azure.Cosmos;
using ResultBoxes;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.MultiProjections;
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

    /// <summary>
    ///     Initializes a new instance of the <see cref="CosmosMultiProjectionStateStore"/> class.
    /// </summary>
    /// <param name="context">The CosmosDB context.</param>
    /// <param name="blobAccessor">Optional blob storage accessor for offloaded state data.</param>
    public CosmosMultiProjectionStateStore(
        CosmosDbContext context,
        IBlobStorageSnapshotAccessor? blobAccessor = null)
    {
        _context = context;
        _blobAccessor = blobAccessor;
    }

    /// <inheritdoc />
    public async Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var container = await _context.GetMultiProjectionStatesContainerAsync().ConfigureAwait(false);
            var partitionKey = $"MultiProjectionState_{projectorName}";

            var response = await container.ReadItemAsync<CosmosMultiProjectionState>(
                projectorVersion,
                new PartitionKey(partitionKey),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var doc = response.Resource;
            if (doc == null)
                return ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty);

            // Load from blob if offloaded
            byte[]? stateData = null;
            if (doc.IsOffloaded && _blobAccessor != null && doc.OffloadKey != null)
            {
                stateData = await _blobAccessor.ReadAsync(doc.OffloadKey, cancellationToken).ConfigureAwait(false);
            }

            return ResultBox.FromValue(OptionalValue.FromValue(doc.ToRecord(stateData)));
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
            var container = await _context.GetMultiProjectionStatesContainerAsync().ConfigureAwait(false);
            var partitionKey = $"MultiProjectionState_{projectorName}";

            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.partitionKey = @pk ORDER BY c.eventsProcessed DESC")
                .WithParameter("@pk", partitionKey);

            var iterator = container.GetItemQueryIterator<CosmosMultiProjectionState>(
                query,
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = new PartitionKey(partitionKey),
                    MaxItemCount = 1
                });

            if (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
                var doc = response.FirstOrDefault();
                if (doc == null)
                    return ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty);

                // Load from blob if offloaded
                byte[]? stateData = null;
                if (doc.IsOffloaded && _blobAccessor != null && doc.OffloadKey != null)
                {
                    stateData = await _blobAccessor.ReadAsync(doc.OffloadKey, cancellationToken).ConfigureAwait(false);
                }

                return ResultBox.FromValue(OptionalValue.FromValue(doc.ToRecord(stateData)));
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
            var container = await _context.GetMultiProjectionStatesContainerAsync().ConfigureAwait(false);
            var partitionKey = record.GetPartitionKey();

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

            var doc = CosmosMultiProjectionState.FromRecord(updatedRecord);

            await container.UpsertItemAsync(
                doc,
                new PartitionKey(partitionKey),
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
            var container = await _context.GetMultiProjectionStatesContainerAsync().ConfigureAwait(false);

            var query = new QueryDefinition("SELECT * FROM c");
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
}
