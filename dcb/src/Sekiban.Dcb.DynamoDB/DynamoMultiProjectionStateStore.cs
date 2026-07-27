using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using ResultBoxes;
using Sekiban.Dcb.DynamoDB.Models;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Storage.Checkpoints;
using Sekiban.Dcb.Capabilities;

namespace Sekiban.Dcb.DynamoDB;

#pragma warning disable CA1031

/// <summary>
///     DynamoDB implementation of IMultiProjectionStateStore. SEK-G20: also the generation/tombstone/exact-token CAS
///     surface (<see cref="IGenerationAwareCheckpointStore" />) via conditional PutItem/UpdateItem — the ConditionExpression
///     on the exact (generation, revision, lifecycle) is the version condition; the required SOURCE lifecycle is hardcoded
///     per op so a tombstone can never be resurrected by a normal persist.
/// </summary>
public class DynamoMultiProjectionStateStore :
    IMultiProjectionStateStore,
    IStorageDurabilityDescriptorProvider,
    IGenerationAwareCheckpointStore
{
    /// <summary>Projection state lands in DynamoDB.</summary>
    public StorageDurabilityDescriptor DescribeStorage() =>
        new(StorageDurability.Durable, "DynamoDB");

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

    // ---------------------------------------------------------------------------------------------------------------
    // SEK-G20 generation/tombstone/exact-token CAS (DynamoDB native — conditional PutItem/UpdateItem version condition)
    // ---------------------------------------------------------------------------------------------------------------

    public CheckpointStoreCapabilityDescriptor DescribeCheckpointCapability() =>
        CheckpointStoreCapabilityDescriptor.Supporting("DynamoDB", CheckpointCapabilityKind.GenerationTombstoneCas);

    private static bool TryToken(CheckpointExpectation e, out long revision) => long.TryParse(e.ExpectedRevision, out revision);

    private static readonly Dictionary<string, string> ControlNames = new()
    {
        ["#g"] = "generation",
        ["#r"] = "revision",
        ["#l"] = "lifecycle",
        ["#u"] = "updatedAt"
    };

    private static Dictionary<string, AttributeValue> ExactTokenValues(long generation, long revision, int lifecycle) => new()
    {
        [":eg"] = new AttributeValue { N = generation.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        [":er"] = new AttributeValue { N = revision.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        [":el"] = new AttributeValue { N = lifecycle.ToString(System.Globalization.CultureInfo.InvariantCulture) }
    };

    public async Task<ResultBox<CheckpointSlot>> ReadCheckpointSlotAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);
            var serviceId = CurrentServiceId;
            var response = await _client.GetItemAsync(new GetItemRequest
            {
                TableName = _context.ProjectionStatesTableName,
                Key = BuildProjectionKey(serviceId, projectorName, projectorVersion),
                ConsistentRead = true
            }, cancellationToken).ConfigureAwait(false);

            if (response.Item == null || response.Item.Count == 0)
            {
                return ResultBox.FromValue(CheckpointSlot.Absent);
            }
            var doc = DynamoMultiProjectionState.FromAttributeValues(response.Item);
            return ResultBox.FromValue(new CheckpointSlot(
                true, doc.Generation, doc.Revision.ToString(), (CheckpointLifecycle)doc.Lifecycle, doc.ToRecord()));
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

    public async Task<CheckpointCasOutcome> ConditionalUpsertAsync(
        MultiProjectionStateWriteRequest payload,
        Stream stream,
        CheckpointExpectation expectation,
        int offloadThresholdBytes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);
            var offload = await StreamOffloadHelper.ProcessAsync(
                stream, $"{payload.ProjectorName}/{payload.ProjectorVersion}", GetEffectiveThreshold(offloadThresholdBytes), _blobAccessor, cancellationToken).ConfigureAwait(false);
            var record = (payload with
            {
                IsOffloaded = offload.IsOffloaded, OffloadKey = offload.OffloadKey,
                OffloadProvider = offload.OffloadProvider, UpdatedAt = DateTime.UtcNow
            }).ToRecord();
            var doc = DynamoMultiProjectionState.FromRecord(record, CurrentServiceId, offload.InlineData);

            if (expectation.ExpectAbsent)
            {
                doc.Generation = 0;
                doc.Revision = 1;
                doc.Lifecycle = (int)CheckpointLifecycle.Active;
                try
                {
                    await _client.PutItemAsync(new PutItemRequest
                    {
                        TableName = _context.ProjectionStatesTableName,
                        Item = doc.ToAttributeValues(),
                        ConditionExpression = "attribute_not_exists(pk)"
                    }, cancellationToken).ConfigureAwait(false);
                    return CheckpointCasOutcome.Committed(await RefetchAsync(payload.ProjectorName, payload.ProjectorVersion, cancellationToken));
                }
                catch (ConditionalCheckFailedException)
                {
                    return CheckpointCasOutcome.Rejected(await RefetchAsync(payload.ProjectorName, payload.ProjectorVersion, cancellationToken));
                }
            }

            if (!TryToken(expectation, out var expectedRevision))
            {
                return CheckpointCasOutcome.Corrupt();
            }

            // Full-item conditional replace on the exact ACTIVE token (source lifecycle hardcoded to Active so a
            // tombstone cannot be resurrected by a normal persist).
            doc.Generation = expectation.ExpectedGeneration;
            doc.Revision = expectedRevision + 1;
            doc.Lifecycle = (int)CheckpointLifecycle.Active;
            try
            {
                await _client.PutItemAsync(new PutItemRequest
                {
                    TableName = _context.ProjectionStatesTableName,
                    Item = doc.ToAttributeValues(),
                    ConditionExpression = "#g = :eg AND #r = :er AND #l = :el",
                    ExpressionAttributeNames = ControlNames,
                    ExpressionAttributeValues = ExactTokenValues(expectation.ExpectedGeneration, expectedRevision, (int)CheckpointLifecycle.Active)
                }, cancellationToken).ConfigureAwait(false);
                return CheckpointCasOutcome.Committed(await RefetchAsync(payload.ProjectorName, payload.ProjectorVersion, cancellationToken));
            }
            catch (ConditionalCheckFailedException)
            {
                return CheckpointCasOutcome.Rejected(await RefetchAsync(payload.ProjectorName, payload.ProjectorVersion, cancellationToken));
            }
        }
        catch (Exception ex)
        {
            // SEK-G20: a dispatch/transport failure whose commit is UNKNOWN — resolve via a bounded independent re-read.
            if (IsDeterministicPreCommitFailure(ex, cancellationToken)) return CheckpointCasOutcome.ProviderFailed(ex);
            return await ResolveWriteInDoubtAsync(payload, expectation.ExpectAbsent ? 0 : expectation.ExpectedGeneration, ex);
        }
    }

    public async Task<CheckpointCasOutcome> InvalidateWithTombstoneAsync(
        string projectorName,
        string projectorVersion,
        CheckpointExpectation expectation,
        CancellationToken cancellationToken = default)
    {
        if (expectation.ExpectAbsent || !TryToken(expectation, out var expectedRevision))
        {
            return CheckpointCasOutcome.Corrupt();
        }
        try
        {
            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);
            var serviceId = CurrentServiceId;

            // Partial UpdateItem retains the payload/offload under the tombstone; bump generation + revision, flip
            // lifecycle to Tombstoned, on the exact ACTIVE token.
            var values = ExactTokenValues(expectation.ExpectedGeneration, expectedRevision, (int)CheckpointLifecycle.Active);
            values[":ng"] = new AttributeValue { N = (expectation.ExpectedGeneration + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) };
            values[":nr"] = new AttributeValue { N = (expectedRevision + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) };
            values[":nl"] = new AttributeValue { N = ((int)CheckpointLifecycle.Tombstoned).ToString(System.Globalization.CultureInfo.InvariantCulture) };
            values[":nu"] = new AttributeValue { S = DateTime.UtcNow.ToString("O") };
            try
            {
                await _client.UpdateItemAsync(new UpdateItemRequest
                {
                    TableName = _context.ProjectionStatesTableName,
                    Key = BuildProjectionKey(serviceId, projectorName, projectorVersion),
                    UpdateExpression = "SET #g = :ng, #r = :nr, #l = :nl, #u = :nu",
                    ConditionExpression = "#g = :eg AND #r = :er AND #l = :el",
                    ExpressionAttributeNames = ControlNames,
                    ExpressionAttributeValues = values
                }, cancellationToken).ConfigureAwait(false);
                return CheckpointCasOutcome.Committed(await RefetchAsync(projectorName, projectorVersion, cancellationToken));
            }
            catch (ConditionalCheckFailedException)
            {
                return CheckpointCasOutcome.Rejected(await RefetchAsync(projectorName, projectorVersion, cancellationToken));
            }
        }
        catch (Exception ex)
        {
            // SEK-G20: a lost response on the tombstone UpdateItem is UNKNOWN-commit — deterministic pre-commit/validation
            // is ProviderFailure, otherwise resolve by a bounded independent re-read (Tombstoned at g+1 => our own commit).
            if (IsDeterministicPreCommitFailure(ex, cancellationToken)) return CheckpointCasOutcome.ProviderFailed(ex);
            return await ResolveInvalidateInDoubtAsync(
                projectorName, projectorVersion, expectation.ExpectedGeneration + 1, expectedRevision + 1, ex);
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
            if (expectation.ExpectAbsent || !TryToken(expectation, out var expectedRevision))
            {
                return CheckpointCasOutcome.Corrupt();
            }
            await _context.EnsureTablesAsync(cancellationToken).ConfigureAwait(false);
            var offload = await StreamOffloadHelper.ProcessAsync(
                stream, $"{payload.ProjectorName}/{payload.ProjectorVersion}", GetEffectiveThreshold(offloadThresholdBytes), _blobAccessor, cancellationToken).ConfigureAwait(false);
            var record = (payload with
            {
                IsOffloaded = offload.IsOffloaded, OffloadKey = offload.OffloadKey,
                OffloadProvider = offload.OffloadProvider, UpdatedAt = DateTime.UtcNow
            }).ToRecord();
            var doc = DynamoMultiProjectionState.FromRecord(record, CurrentServiceId, offload.InlineData);
            // One atomic conditional put on the exact TOMBSTONED token: rebuilt payload AND clear the tombstone.
            doc.Generation = expectation.ExpectedGeneration;
            doc.Revision = expectedRevision + 1;
            doc.Lifecycle = (int)CheckpointLifecycle.Active;
            try
            {
                await _client.PutItemAsync(new PutItemRequest
                {
                    TableName = _context.ProjectionStatesTableName,
                    Item = doc.ToAttributeValues(),
                    ConditionExpression = "#g = :eg AND #r = :er AND #l = :el",
                    ExpressionAttributeNames = ControlNames,
                    ExpressionAttributeValues = ExactTokenValues(expectation.ExpectedGeneration, expectedRevision, (int)CheckpointLifecycle.Tombstoned)
                }, cancellationToken).ConfigureAwait(false);
                return CheckpointCasOutcome.Committed(await RefetchAsync(payload.ProjectorName, payload.ProjectorVersion, cancellationToken));
            }
            catch (ConditionalCheckFailedException)
            {
                return CheckpointCasOutcome.Rejected(await RefetchAsync(payload.ProjectorName, payload.ProjectorVersion, cancellationToken));
            }
        }
        catch (Exception ex)
        {
            // SEK-G20: a dispatch/transport failure whose commit is UNKNOWN — resolve via a bounded independent re-read.
            if (IsDeterministicPreCommitFailure(ex, cancellationToken)) return CheckpointCasOutcome.ProviderFailed(ex);
            return await ResolveWriteInDoubtAsync(payload, expectation.ExpectedGeneration, ex);
        }
    }

    // A DETERMINISTIC pre-commit failure — a validation error (malformed request / missing table) or an already-cancelled
    // token. Provably NOT post-commit, so ProviderFailure/fail-closed, never in-doubt.
    private static bool IsDeterministicPreCommitFailure(Exception ex, CancellationToken ct)
    {
        if (ct.IsCancellationRequested && ex is OperationCanceledException)
        {
            return true;
        }
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is Amazon.DynamoDBv2.Model.ResourceNotFoundException
                || (e is Amazon.Runtime.AmazonServiceException ase
                    && string.Equals(ase.ErrorCode, "ValidationException", StringComparison.Ordinal)))
            {
                return true;
            }
        }
        return false;
    }

    // Resolves a write whose response was lost: a bounded re-read that verifies our exact resulting generation + Active
    // lifecycle + payload identity reports Committed; otherwise typed retryable InDoubt (retry with the same exact token
    // is idempotent). Never a false Committed — a concurrent winner with a different payload fails identity.
    private Task<CheckpointCasOutcome> ResolveWriteInDoubtAsync(
        MultiProjectionStateWriteRequest payload, long resultingGeneration, Exception cause) =>
        CheckpointInDoubtResolver.ResolveAsync(
            ct => ReadCheckpointSlotAsync(payload.ProjectorName, payload.ProjectorVersion, ct),
            slot => slot.IsActive && slot.Generation == resultingGeneration && slot.Record is { } r
                && string.Equals(r.LastSortableUniqueId, payload.LastSortableUniqueId, StringComparison.Ordinal)
                && r.EventsProcessed == payload.EventsProcessed,
            maxAttempts: 3,
            cause: cause);

    // Resolves a tombstone (invalidate) write whose response was lost: only an invalidate from the exact Active token we
    // observed can produce Tombstoned at the resulting (generation, revision), so a re-read that shows it confirms our
    // own commit; an unconfirmable re-read is typed retryable InDoubt.
    private Task<CheckpointCasOutcome> ResolveInvalidateInDoubtAsync(
        string projectorName, string projectorVersion, long resultingGeneration, long resultingRevision, Exception cause) =>
        CheckpointInDoubtResolver.ResolveAsync(
            ct => ReadCheckpointSlotAsync(projectorName, projectorVersion, ct),
            slot => slot.IsTombstoned && slot.Generation == resultingGeneration
                && string.Equals(slot.Revision, resultingRevision.ToString(), StringComparison.Ordinal),
            maxAttempts: 3,
            cause: cause);
}
