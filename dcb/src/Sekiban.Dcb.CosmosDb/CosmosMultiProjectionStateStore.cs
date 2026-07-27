using System.Net;
using Microsoft.Azure.Cosmos;
using ResultBoxes;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Storage.Checkpoints;
using Sekiban.Dcb.Capabilities;

namespace Sekiban.Dcb.CosmosDb;

/// <summary>
///     Cosmos DB implementation of IMultiProjectionStateStore. SEK-G20: also the generation/tombstone/exact-token CAS
///     surface (<see cref="IGenerationAwareCheckpointStore" />). The Cosmos <c>_etag</c> IS the exact per-mutation token
///     (IfMatch); generation + lifecycle are item fields (absent on pre-G20 docs → default 0 = generation 0, Active).
/// </summary>
public class CosmosMultiProjectionStateStore :
    IMultiProjectionStateStore,
    IStorageDurabilityDescriptorProvider,
    IGenerationAwareCheckpointStore
{
    /// <summary>Projection state lands in Cosmos DB.</summary>
    public StorageDurabilityDescriptor DescribeStorage() =>
        new(StorageDurability.Durable, "CosmosDb");

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

    private static string GetPartitionKey(string partitionKey, string serviceId) =>
        $"{serviceId}|{partitionKey}";

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
            var pk = GetPartitionKey(partitionKey, serviceId);

            var response = await container.ReadItemAsync<CosmosMultiProjectionState>(
                projectorVersion,
                new PartitionKey(pk),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var doc = response.Resource;
            if (doc == null)
            {
                return ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty);
            }

            return await BuildRecordResultAsync(
                doc,
                serviceId,
                cancellationToken).ConfigureAwait(false);
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
            var pk = GetPartitionKey(partitionKey, serviceId);

            var query = new QueryDefinition(
                    "SELECT * FROM c WHERE c.pk = @pk ORDER BY c.eventsProcessed DESC")
                .WithParameter("@pk", pk);

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
                return await BuildRecordResultAsync(
                    doc,
                    serviceId,
                    cancellationToken).ConfigureAwait(false);
            }

            return ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty);
        }
    }

    private static Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> BuildRecordResultAsync(
        CosmosMultiProjectionState doc,
        string serviceId,
        CancellationToken _cancellationToken)
    {
        var validationError = ValidateServiceId(doc, serviceId);
        if (validationError != null)
        {
            return Task.FromResult(ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(validationError));
        }
        return Task.FromResult(ResultBox.FromValue(OptionalValue.FromValue(doc.ToRecord())));
    }

    private static UnauthorizedAccessException? ValidateServiceId(
        CosmosMultiProjectionState doc,
        string serviceId)
    {
        return string.Equals(doc.ServiceId, serviceId, StringComparison.Ordinal)
            ? null
            : new UnauthorizedAccessException(
                $"Projection state does not belong to service {serviceId}.");
    }

    /// <summary>
    ///     Opens snapshot payload stream from offload storage or inline Cosmos document data.
    /// </summary>
    public async Task<ResultBox<Stream>> OpenStateDataReadStreamAsync(
        MultiProjectionStateRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.IsOffloaded && _blobAccessor != null && !string.IsNullOrWhiteSpace(record.OffloadKey))
        {
            try
            {
                var stream = await _blobAccessor.OpenReadAsync(record.OffloadKey, cancellationToken)
                    .ConfigureAwait(false);
                return ResultBox.FromValue(stream);
            }
            catch (InvalidOperationException ex)
            {
                return ResultBox.Error<Stream>(
                    new InvalidOperationException(
                        $"Failed to open offloaded state stream. Projector: {record.ProjectorName}, Version: {record.ProjectorVersion}, BlobKey: {record.OffloadKey}",
                        ex));
            }
            catch (IOException ex)
            {
                return ResultBox.Error<Stream>(
                    new InvalidOperationException(
                        $"Failed to open offloaded state stream. Projector: {record.ProjectorName}, Version: {record.ProjectorVersion}, BlobKey: {record.OffloadKey}",
                        ex));
            }
        }

        var serviceId = CurrentServiceId;
        var settings = _containerResolver.ResolveStatesContainer(serviceId);
        var container = await _context.GetMultiProjectionStatesContainerAsync(settings).ConfigureAwait(false);
        var partitionKey = $"MultiProjectionState_{record.ProjectorName}";
        var pk = GetPartitionKey(partitionKey, serviceId);
        CosmosMultiProjectionState doc;
        try
        {
            var response = await container.ReadItemAsync<CosmosMultiProjectionState>(
                record.ProjectorVersion,
                new PartitionKey(pk),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            doc = response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return ResultBox.Error<Stream>(
                new KeyNotFoundException(
                    $"Projection state not found: {record.ProjectorName}/{record.ProjectorVersion}",
                    ex));
        }

        var validationError = ValidateServiceId(doc, serviceId);
        if (validationError is not null)
        {
            return ResultBox.Error<Stream>(validationError);
        }

        if (!string.IsNullOrWhiteSpace(doc.StateData))
        {
            var bytes = Convert.FromBase64String(doc.StateData);
            return ResultBox.FromValue<Stream>(new MemoryStream(bytes, writable: false));
        }

        return ResultBox.Error<Stream>(
            new InvalidOperationException(
                $"Projection state has no inline data and no readable offload stream: {record.ProjectorName}/{record.ProjectorVersion}"));
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
            if (!record.IsOffloaded)
            {
                return ResultBox.Error<bool>(
                    new NotSupportedException(
                        "UpsertAsync without payload stream is not supported for non-offloaded snapshots. Use UpsertFromStreamAsync."));
            }

            var updatedRecord = record with { UpdatedAt = DateTime.UtcNow };

            await PersistRecordToCosmosAsync(updatedRecord, stateData: null, cancellationToken).ConfigureAwait(false);
            return ResultBox.FromValue(true);
        }
        catch (CosmosException ex)
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
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var effectiveThreshold = _context.Options
                .GetEffectiveMultiProjectionStateOffloadThresholdBytes(offloadThresholdBytes);

            var offloadResult = await StreamOffloadHelper.ProcessAsync(
                stream,
                $"{request.ProjectorName}/{request.ProjectorVersion}",
                effectiveThreshold,
                _blobAccessor,
                cancellationToken).ConfigureAwait(false);

            var record = (request with
            {
                IsOffloaded = offloadResult.IsOffloaded,
                OffloadKey = offloadResult.OffloadKey,
                OffloadProvider = offloadResult.OffloadProvider,
                UpdatedAt = DateTime.UtcNow
            }).ToRecord();

            await PersistRecordToCosmosAsync(record, offloadResult.InlineData, cancellationToken).ConfigureAwait(false);
            return ResultBox.FromValue(true);
        }
        catch (CosmosException ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    private async Task PersistRecordToCosmosAsync(
        MultiProjectionStateRecord record,
        byte[]? stateData,
        CancellationToken cancellationToken)
    {
        var serviceId = CurrentServiceId;
        var settings = _containerResolver.ResolveStatesContainer(serviceId);
        var container = await _context.GetMultiProjectionStatesContainerAsync(settings).ConfigureAwait(false);
        var partitionKey = record.GetPartitionKey();
        var pk = GetPartitionKey(partitionKey, serviceId);

        var doc = CosmosMultiProjectionState.FromRecord(record, serviceId, stateData);

        await container.UpsertItemAsync(
            doc,
            new PartitionKey(pk),
            cancellationToken: cancellationToken).ConfigureAwait(false);
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

            var query = new QueryDefinition("SELECT * FROM c WHERE c.serviceId = @serviceId")
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
            var pk = GetPartitionKey(partitionKey, serviceId);

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
                var pk = GetPartitionKey(partitionKey, serviceId);
                query = new QueryDefinition("SELECT * FROM c WHERE c.pk = @pk")
                    .WithParameter("@pk", pk);
            }
            else
            {
                query = new QueryDefinition("SELECT * FROM c WHERE c.serviceId = @serviceId")
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
                        new PartitionKey(doc.Pk),
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

    private static Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken) =>
        StreamReadHelper.ReadAllBytesAsync(stream, cancellationToken);

    // ---------------------------------------------------------------------------------------------------------------
    // SEK-G20 generation/tombstone/exact-token CAS (Cosmos native — ETag IfMatch is the exact token)
    // ---------------------------------------------------------------------------------------------------------------

    public CheckpointStoreCapabilityDescriptor DescribeCheckpointCapability() =>
        CheckpointStoreCapabilityDescriptor.Supporting("CosmosDb", CheckpointCapabilityKind.GenerationTombstoneCas);

    private async Task<(Container Container, string Pk)> ResolveAsync(string projectorName)
    {
        var serviceId = CurrentServiceId;
        var settings = _containerResolver.ResolveStatesContainer(serviceId);
        var container = await _context.GetMultiProjectionStatesContainerAsync(settings).ConfigureAwait(false);
        var pk = GetPartitionKey($"MultiProjectionState_{projectorName}", serviceId);
        return (container, pk);
    }

    private static CheckpointSlot SlotFrom(CosmosMultiProjectionState doc) => new(
        true, doc.Generation, doc.ETag ?? string.Empty, (CheckpointLifecycle)doc.Lifecycle, doc.ToRecord());

    public async Task<ResultBox<CheckpointSlot>> ReadCheckpointSlotAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (container, pk) = await ResolveAsync(projectorName);
            var response = await container.ReadItemAsync<CosmosMultiProjectionState>(
                projectorVersion, new PartitionKey(pk), cancellationToken: cancellationToken).ConfigureAwait(false);
            return ResultBox.FromValue(SlotFrom(response.Resource));
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return ResultBox.FromValue(CheckpointSlot.Absent);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<CheckpointSlot>(ex);
        }
    }

    private async Task<CheckpointSlot> RefetchAsync(string projectorName, string projectorVersion, CancellationToken ct)
    {
        var read = await ReadCheckpointSlotAsync(projectorName, projectorVersion, ct);
        return read.IsSuccess ? read.GetValue() : CheckpointSlot.Absent;
    }

    private async Task<CosmosMultiProjectionState> BuildDocAsync(
        MultiProjectionStateWriteRequest payload, Stream stream, int offloadThresholdBytes, CancellationToken ct)
    {
        var effectiveThreshold = _context.Options.GetEffectiveMultiProjectionStateOffloadThresholdBytes(offloadThresholdBytes);
        var offload = await StreamOffloadHelper.ProcessAsync(
            stream, $"{payload.ProjectorName}/{payload.ProjectorVersion}", effectiveThreshold, _blobAccessor, ct).ConfigureAwait(false);
        var record = (payload with
        {
            IsOffloaded = offload.IsOffloaded,
            OffloadKey = offload.OffloadKey,
            OffloadProvider = offload.OffloadProvider,
            UpdatedAt = DateTime.UtcNow
        }).ToRecord();
        return CosmosMultiProjectionState.FromRecord(record, CurrentServiceId, offload.InlineData);
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
            var (container, pk) = await ResolveAsync(payload.ProjectorName);
            var doc = await BuildDocAsync(payload, stream, offloadThresholdBytes, cancellationToken);

            if (expectation.ExpectAbsent)
            {
                doc.Generation = 0;
                doc.Lifecycle = (int)CheckpointLifecycle.Active;
                try
                {
                    await container.CreateItemAsync(doc, new PartitionKey(pk), cancellationToken: cancellationToken).ConfigureAwait(false);
                    // Re-read to obtain the authoritative committed ETag/generation/lifecycle (the exact token).
                    return CheckpointCasOutcome.Committed(await RefetchAsync(payload.ProjectorName, payload.ProjectorVersion, cancellationToken));
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
                {
                    return CheckpointCasOutcome.Rejected(await RefetchAsync(payload.ProjectorName, payload.ProjectorVersion, cancellationToken));
                }
            }

            // A normal persist only advances an ACTIVE row. The ETag IfMatch alone does not encode lifecycle, so guard on
            // the observed lifecycle: a Tombstoned expectation must not be resurrected by a normal upsert (only
            // CommitRebuilt may). This mirrors the "AND Lifecycle = Active" WHERE clause the SQL providers use.
            if (expectation.ExpectedLifecycle != CheckpointLifecycle.Active)
            {
                return CheckpointCasOutcome.Rejected(await RefetchAsync(payload.ProjectorName, payload.ProjectorVersion, cancellationToken));
            }

            // Update on the exact ETag (IfMatch). Preserve the observed generation; stay Active.
            doc.Generation = expectation.ExpectedGeneration;
            doc.Lifecycle = (int)CheckpointLifecycle.Active;
            return await ReplaceIfMatchAsync(container, pk, doc, expectation, payload.ProjectorName, payload.ProjectorVersion, cancellationToken);
        }
        catch (Exception ex)
        {
            return CheckpointCasOutcome.ProviderFailed(ex);
        }
    }

    public async Task<CheckpointCasOutcome> InvalidateWithTombstoneAsync(
        string projectorName,
        string projectorVersion,
        CheckpointExpectation expectation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (expectation.ExpectAbsent || expectation.ExpectedLifecycle != CheckpointLifecycle.Active)
            {
                // Only an Active row is invalidated; a non-Active expectation is stale/invalid.
                return expectation.ExpectAbsent
                    ? CheckpointCasOutcome.Corrupt()
                    : CheckpointCasOutcome.Rejected(await RefetchAsync(projectorName, projectorVersion, cancellationToken));
            }
            var (container, pk) = await ResolveAsync(projectorName);

            // Read the current doc to retain its payload/offload under the tombstone; the IfMatch on the expected ETag is
            // the CAS (a concurrent writer that moved the ETag makes the Replace fail 412).
            CosmosMultiProjectionState doc;
            try
            {
                var response = await container.ReadItemAsync<CosmosMultiProjectionState>(
                    projectorVersion, new PartitionKey(pk), cancellationToken: cancellationToken).ConfigureAwait(false);
                doc = response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return CheckpointCasOutcome.Rejected(CheckpointSlot.Absent);
            }

            doc.Generation = expectation.ExpectedGeneration + 1;
            doc.Lifecycle = (int)CheckpointLifecycle.Tombstoned;
            doc.UpdatedAt = DateTime.UtcNow;
            return await ReplaceIfMatchAsync(container, pk, doc, expectation, projectorName, projectorVersion, cancellationToken);
        }
        catch (Exception ex)
        {
            return CheckpointCasOutcome.ProviderFailed(ex);
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
            if (expectation.ExpectAbsent)
            {
                return CheckpointCasOutcome.Corrupt();
            }
            if (expectation.ExpectedLifecycle != CheckpointLifecycle.Tombstoned)
            {
                // A rebuilt commit only advances a TOMBSTONED row; any other observed lifecycle is stale/invalid.
                return CheckpointCasOutcome.Rejected(await RefetchAsync(payload.ProjectorName, payload.ProjectorVersion, cancellationToken));
            }
            var (container, pk) = await ResolveAsync(payload.ProjectorName);
            var doc = await BuildDocAsync(payload, stream, offloadThresholdBytes, cancellationToken);
            // One atomic same-row CAS (IfMatch tombstone ETag): rebuilt payload AND clear the tombstone at generation g+1.
            doc.Generation = expectation.ExpectedGeneration;
            doc.Lifecycle = (int)CheckpointLifecycle.Active;
            return await ReplaceIfMatchAsync(container, pk, doc, expectation, payload.ProjectorName, payload.ProjectorVersion, cancellationToken);
        }
        catch (Exception ex)
        {
            return CheckpointCasOutcome.ProviderFailed(ex);
        }
    }

    private async Task<CheckpointCasOutcome> ReplaceIfMatchAsync(
        Container container,
        string pk,
        CosmosMultiProjectionState doc,
        CheckpointExpectation expectation,
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken)
    {
        try
        {
            await container.ReplaceItemAsync(
                doc, doc.Id, new PartitionKey(pk),
                new ItemRequestOptions { IfMatchEtag = expectation.ExpectedRevision },
                cancellationToken).ConfigureAwait(false);
            // Re-read to obtain the authoritative committed ETag (the new exact token).
            return CheckpointCasOutcome.Committed(await RefetchAsync(projectorName, projectorVersion, cancellationToken));
        }
        catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            return CheckpointCasOutcome.Rejected(await RefetchAsync(projectorName, projectorVersion, cancellationToken));
        }
    }
}
