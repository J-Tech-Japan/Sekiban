using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using ResultBoxes;
using Sekiban.Dcb.DynamoDB.Models;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.DynamoDB;

/// <summary>
///     DynamoDB implementation of IMultiProjectionStateStore.
/// </summary>
public class DynamoMultiProjectionStateStore : IMultiProjectionStateStore
{
    private readonly DynamoDbContext _context;
    private readonly IBlobStorageSnapshotAccessor? _blobAccessor;
    private readonly DynamoDbEventStoreOptions _options;
    private readonly IAmazonDynamoDB _client;

    public DynamoMultiProjectionStateStore(
        DynamoDbContext context,
        IBlobStorageSnapshotAccessor? blobAccessor = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _blobAccessor = blobAccessor;
        _options = context.Options;
        _client = context.Client;
    }

    public async Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);

            var key = BuildProjectionKey(projectorName, projectorVersion);
            var response = await _client.GetItemAsync(new GetItemRequest
            {
                TableName = _context.ProjectionStatesTableName,
                Key = key,
                ConsistentRead = _options.UseConsistentReads
            }, cancellationToken).ConfigureAwait(false);

            if (response.Item == null || response.Item.Count == 0)
                return ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty);

            var doc = DynamoMultiProjectionState.FromAttributeValues(response.Item);

            byte[]? stateData = null;
            if (doc.IsOffloaded && _blobAccessor != null && doc.OffloadKey != null)
            {
                try
                {
                    stateData = await _blobAccessor.ReadAsync(doc.OffloadKey, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(
                        new InvalidOperationException(
                            $"Failed to read offloaded state from blob storage. " +
                            $"Projector: {projectorName}, Version: {projectorVersion}, BlobKey: {doc.OffloadKey}",
                            ex));
                }
            }

            return ResultBox.FromValue(OptionalValue.FromValue(doc.ToRecord(stateData)));
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
            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);

            var items = await QueryProjectionItemsAsync(projectorName, cancellationToken).ConfigureAwait(false);
            if (items.Count == 0)
                return ResultBox.FromValue(OptionalValue<MultiProjectionStateRecord>.Empty);

            var latest = items.OrderByDescending(i => i.EventsProcessed).First();

            byte[]? stateData = null;
            if (latest.IsOffloaded && _blobAccessor != null && latest.OffloadKey != null)
            {
                try
                {
                    stateData = await _blobAccessor.ReadAsync(latest.OffloadKey, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return ResultBox.Error<OptionalValue<MultiProjectionStateRecord>>(
                        new InvalidOperationException(
                            $"Failed to read offloaded state from blob storage. " +
                            $"Projector: {projectorName}, BlobKey: {latest.OffloadKey}",
                            ex));
                }
            }

            return ResultBox.FromValue(OptionalValue.FromValue(latest.ToRecord(stateData)));
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
        ArgumentNullException.ThrowIfNull(record);

        try
        {
            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);

            var stateData = record.StateData;
            var isOffloaded = record.IsOffloaded;
            string? offloadKey = record.OffloadKey;
            string? offloadProvider = record.OffloadProvider;

            if (stateData != null && stateData.Length > offloadThresholdBytes && _blobAccessor != null)
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

            var doc = DynamoMultiProjectionState.FromRecord(updatedRecord);

            await _client.PutItemAsync(new PutItemRequest
            {
                TableName = _context.ProjectionStatesTableName,
                Item = doc.ToAttributeValues()
            }, cancellationToken).ConfigureAwait(false);

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
            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);

            var list = new List<ProjectorStateInfo>();
            Dictionary<string, AttributeValue>? lastKey = null;

            do
            {
                var response = await _client.ScanAsync(new ScanRequest
                {
                    TableName = _context.ProjectionStatesTableName,
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

    public async Task<ResultBox<bool>> DeleteAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);

            var key = BuildProjectionKey(projectorName, projectorVersion);
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

    public async Task<ResultBox<int>> DeleteAllAsync(
        string? projectorName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);

            var items = projectorName != null
                ? await QueryProjectionItemsAsync(projectorName, cancellationToken).ConfigureAwait(false)
                : await ScanProjectionItemsAsync(cancellationToken).ConfigureAwait(false);

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

    private async Task<List<DynamoMultiProjectionState>> QueryProjectionItemsAsync(
        string projectorName,
        CancellationToken cancellationToken)
    {
        var items = new List<DynamoMultiProjectionState>();
        Dictionary<string, AttributeValue>? lastKey = null;
        var pk = $"PROJECTOR#{projectorName}";

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

    private async Task<List<DynamoMultiProjectionState>> ScanProjectionItemsAsync(CancellationToken cancellationToken)
    {
        var items = new List<DynamoMultiProjectionState>();
        Dictionary<string, AttributeValue>? lastKey = null;

        do
        {
            var response = await _client.ScanAsync(new ScanRequest
            {
                TableName = _context.ProjectionStatesTableName,
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

    private static Dictionary<string, AttributeValue> BuildProjectionKey(string projectorName, string projectorVersion)
    {
        return new Dictionary<string, AttributeValue>
        {
            ["pk"] = new AttributeValue { S = $"PROJECTOR#{projectorName}" },
            ["sk"] = new AttributeValue { S = $"VERSION#{projectorVersion}" }
        };
    }
}
