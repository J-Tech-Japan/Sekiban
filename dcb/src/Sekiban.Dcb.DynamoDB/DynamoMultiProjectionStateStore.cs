using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using ResultBoxes;
using Sekiban.Dcb.DynamoDB.Models;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.DynamoDB;

#pragma warning disable CA1031

/// <summary>
///     DynamoDB implementation of IMultiProjectionStateStore.
/// </summary>
public class DynamoMultiProjectionStateStore : IMultiProjectionStateStore
{
    private readonly DynamoDbContext _context;
    private readonly IBlobStorageSnapshotAccessor? _blobAccessor;
    private readonly DynamoDbEventStoreOptions _options;
    private readonly IAmazonDynamoDB _client;
    private readonly IServiceIdProvider _serviceIdProvider;

    /// <summary>
    ///     Initializes a new DynamoMultiProjectionStateStore.
    /// </summary>
    public DynamoMultiProjectionStateStore(
        DynamoDbContext context,
        IServiceIdProvider serviceIdProvider,
        IBlobStorageSnapshotAccessor? blobAccessor = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _serviceIdProvider = serviceIdProvider ?? throw new ArgumentNullException(nameof(serviceIdProvider));
        _blobAccessor = blobAccessor;
        _options = context.Options;
        _client = context.Client;
    }

    private string CurrentServiceId => _serviceIdProvider.GetCurrentServiceId();

    /// <summary>
    ///     Gets the latest projection state for a specific version.
    /// </summary>
    public async Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);

            var serviceId = CurrentServiceId;
            var key = BuildProjectionKey(serviceId, projectorName, projectorVersion);
            var response = await _client.GetItemAsync(new GetItemRequest
            {
                TableName = _context.ProjectionStatesTableName,
                Key = key,
                ConsistentRead = _options.UseConsistentReads
            }, cancellationToken).ConfigureAwait(false);

            if (response.Item == null || response.Item.Count == 0)
                return ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty);

            var doc = DynamoMultiProjectionState.FromAttributeValues(response.Item);

            return ResultBox.FromValue(OptionalValue.FromValue(doc.ToRecord()));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(ex);
        }
    }

    /// <summary>
    ///     Gets the latest projection state for any version of a projector.
    /// </summary>
    public async Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(
        string projectorName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);

            var serviceId = CurrentServiceId;
            var items = await QueryProjectionItemsAsync(serviceId, projectorName, cancellationToken).ConfigureAwait(false);
            if (items.Count == 0)
                return ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty);

            var latest = items.OrderByDescending(i => i.EventsProcessed).First();

            return ResultBox.FromValue(OptionalValue.FromValue(latest.ToRecord()));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(ex);
        }
    }

    /// <summary>
    ///     Inserts or updates a projection state record.
    /// </summary>
    public async Task<ResultBox<bool>> UpsertAsync(
        MultiProjectionStateRecord record,
        int offloadThresholdBytes = 1_000_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        try
        {
            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);

            if (!record.IsOffloaded)
            {
                return ResultBox.Error<bool>(
                    new NotSupportedException(
                        "UpsertAsync without payload stream is not supported for non-offloaded snapshots. Use UpsertFromStreamAsync."));
            }

            await PersistRecordToDynamoAsync(record with { UpdatedAt = DateTime.UtcNow }, stateData: null, cancellationToken)
                .ConfigureAwait(false);

            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    /// <summary>
    ///     Opens a read stream for snapshot payload bytes either from offload storage or inline DynamoDB data.
    /// </summary>
    /// <param name="record">Snapshot metadata record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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
            catch (IOException ex)
            {
                return ResultBox.Error<Stream>(
                    new InvalidOperationException(
                        $"Failed to open offloaded state stream. Projector: {record.ProjectorName}, Version: {record.ProjectorVersion}, BlobKey: {record.OffloadKey}",
                        ex));
            }
            catch (InvalidOperationException ex)
            {
                return ResultBox.Error<Stream>(
                    new InvalidOperationException(
                        $"Failed to open offloaded state stream. Projector: {record.ProjectorName}, Version: {record.ProjectorVersion}, BlobKey: {record.OffloadKey}",
                        ex));
            }
        }

        await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);
        var serviceId = CurrentServiceId;
        var key = BuildProjectionKey(serviceId, record.ProjectorName, record.ProjectorVersion);
        var response = await _client.GetItemAsync(new GetItemRequest
        {
            TableName = _context.ProjectionStatesTableName,
            Key = key,
            ConsistentRead = _options.UseConsistentReads
        }, cancellationToken).ConfigureAwait(false);

        if (response.Item != null && response.Item.Count > 0)
        {
            var doc = DynamoMultiProjectionState.FromAttributeValues(response.Item);
            if (!string.IsNullOrWhiteSpace(doc.StateData))
            {
                var bytes = Convert.FromBase64String(doc.StateData);
                return ResultBox.FromValue<Stream>(new MemoryStream(bytes, writable: false));
            }
        }

        return ResultBox.Error<Stream>(
            new InvalidOperationException(
                $"Projection state has no inline data and no readable offload stream: {record.ProjectorName}/{record.ProjectorVersion}"));
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
            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);

            var effectiveThreshold = GetEffectiveThreshold(offloadThresholdBytes);

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

            await PersistRecordToDynamoAsync(record, offloadResult.InlineData, cancellationToken).ConfigureAwait(false);

            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    /// <summary>
    ///     Lists all projection state records.
    /// </summary>
    public async Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);

            var list = new List<ProjectorStateInfo>();
            Dictionary<string, AttributeValue>? lastKey = null;
            var serviceId = CurrentServiceId;

            do
            {
                var response = await _client.ScanAsync(new ScanRequest
                {
                    TableName = _context.ProjectionStatesTableName,
                    FilterExpression = "serviceId = :serviceId",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        [":serviceId"] = new AttributeValue { S = serviceId }
                    },
                    ExclusiveStartKey = lastKey,
                    Limit = _options.QueryPageSize
                }, cancellationToken).ConfigureAwait(false);

                foreach (var item in response.Items)
                {
                    var doc = DynamoMultiProjectionState.FromAttributeValues(item);
                    list.Add(new ProjectorStateInfo(
                        doc.ProjectorName,
                        doc.ProjectorVersion,
                        doc.EventsProcessed,
                        DateTime.TryParse(doc.UpdatedAt, out var updated) ? updated : DateTime.UtcNow,
                        doc.OriginalSizeBytes,
                        doc.CompressedSizeBytes,
                        doc.LastSortableUniqueId));
                }

                lastKey = response.LastEvaluatedKey;
            } while (lastKey != null && lastKey.Count > 0);

            return ResultBox.FromValue<IReadOnlyList<ProjectorStateInfo>>(list);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IReadOnlyList<ProjectorStateInfo>>(ex);
        }
    }

    /// <summary>
    ///     Deletes a projection state record for a specific version.
    /// </summary>
    public async Task<ResultBox<bool>> DeleteAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);

            var serviceId = CurrentServiceId;
            var key = BuildProjectionKey(serviceId, projectorName, projectorVersion);
            var response = await _client.GetItemAsync(new GetItemRequest
            {
                TableName = _context.ProjectionStatesTableName,
                Key = key,
                ConsistentRead = true
            }, cancellationToken).ConfigureAwait(false);

            if (response.Item == null || response.Item.Count == 0)
                return ResultBox.FromValue(false);

            await _client.DeleteItemAsync(new DeleteItemRequest
            {
                TableName = _context.ProjectionStatesTableName,
                Key = key
            }, cancellationToken).ConfigureAwait(false);

            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    /// <summary>
    ///     Deletes all projection state records for a projector (or all projectors if null).
    /// </summary>
    public async Task<ResultBox<int>> DeleteAllAsync(
        string? projectorName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);

            var serviceId = CurrentServiceId;
            var items = projectorName != null
                ? await QueryProjectionItemsAsync(serviceId, projectorName, cancellationToken).ConfigureAwait(false)
                : await ScanProjectionItemsAsync(serviceId, cancellationToken).ConfigureAwait(false);

            foreach (var item in items)
            {
                await _client.DeleteItemAsync(new DeleteItemRequest
                {
                    TableName = _context.ProjectionStatesTableName,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        ["pk"] = new AttributeValue { S = item.Pk },
                        ["sk"] = new AttributeValue { S = item.Sk }
                    }
                }, cancellationToken).ConfigureAwait(false);
            }

            return ResultBox.FromValue(items.Count);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<int>(ex);
        }
    }

    /// <summary>
    ///     Calculates effective offload threshold, taking DynamoDB-specific option override into account.
    /// </summary>
    private int GetEffectiveThreshold(int offloadThresholdBytes)
    {
        var effectiveThreshold = offloadThresholdBytes;
        if (_options.OffloadThresholdBytes > 0 && _options.OffloadThresholdBytes < effectiveThreshold)
        {
            effectiveThreshold = (int)Math.Min(_options.OffloadThresholdBytes, int.MaxValue);
        }
        return effectiveThreshold;
    }

    /// <summary>
    ///     Persists a MultiProjectionStateRecord to DynamoDB via PutItem.
    /// </summary>
    private async Task PersistRecordToDynamoAsync(
        MultiProjectionStateRecord record,
        byte[]? stateData,
        CancellationToken cancellationToken)
    {
        var serviceId = CurrentServiceId;
        var doc = DynamoMultiProjectionState.FromRecord(record, serviceId, stateData);

        await _client.PutItemAsync(new PutItemRequest
        {
            TableName = _context.ProjectionStatesTableName,
            Item = doc.ToAttributeValues()
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<DynamoMultiProjectionState>> QueryProjectionItemsAsync(
        string serviceId,
        string projectorName,
        CancellationToken cancellationToken)
    {
        var items = new List<DynamoMultiProjectionState>();
        Dictionary<string, AttributeValue>? lastKey = null;
        var pk = BuildProjectorPk(serviceId, projectorName);

        do
        {
            var response = await _client.QueryAsync(new QueryRequest
            {
                TableName = _context.ProjectionStatesTableName,
                KeyConditionExpression = "pk = :pk",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":pk"] = new AttributeValue { S = pk }
                },
                ExclusiveStartKey = lastKey,
                Limit = _options.QueryPageSize
            }, cancellationToken).ConfigureAwait(false);

            foreach (var item in response.Items)
            {
                items.Add(DynamoMultiProjectionState.FromAttributeValues(item));
            }

            lastKey = response.LastEvaluatedKey;
        } while (lastKey != null && lastKey.Count > 0);

        return items;
    }

    private async Task<List<DynamoMultiProjectionState>> ScanProjectionItemsAsync(
        string serviceId,
        CancellationToken cancellationToken)
    {
        var items = new List<DynamoMultiProjectionState>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = await _client.ScanAsync(new ScanRequest
            {
                TableName = _context.ProjectionStatesTableName,
                FilterExpression = "serviceId = :serviceId",
                ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                {
                    [":serviceId"] = new AttributeValue { S = serviceId }
                },
                ExclusiveStartKey = lastKey,
                Limit = _options.QueryPageSize
            }, cancellationToken).ConfigureAwait(false);

            foreach (var item in response.Items)
            {
                items.Add(DynamoMultiProjectionState.FromAttributeValues(item));
            }

            lastKey = response.LastEvaluatedKey;
        } while (lastKey != null && lastKey.Count > 0);

        return items;
    }

    private static Dictionary<string, AttributeValue> BuildProjectionKey(
        string serviceId,
        string projectorName,
        string projectorVersion)
    {
        return new Dictionary<string, AttributeValue>
        {
            ["pk"] = new AttributeValue { S = BuildProjectorPk(serviceId, projectorName) },
            ["sk"] = new AttributeValue { S = $"VERSION#{projectorVersion}" }
        };
    }

    private static string BuildProjectorPk(string serviceId, string projectorName) =>
        $"SERVICE#{serviceId}#PROJECTOR#{projectorName}";

    private static Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken) =>
        StreamReadHelper.ReadAllBytesAsync(stream, cancellationToken);
}
