using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Dcb.Domain;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     SEK-G16 DynamoDB conditional (unique-key) append, driven end to end through the real
///     <see cref="DynamoDbEventStore" /> against an in-process, thread-safe fake <see cref="IAmazonDynamoDB" />. The claim
///     event is written under the deterministic id, so the existing item primary key is the uniqueness primitive (no
///     schema change), enforced by <c>attribute_not_exists(pk)</c>. The fake's check-and-apply is serialized under a lock,
///     so it reproduces DynamoDB's atomic conditional put exactly — which lets the N-writer test race real concurrent
///     transactions. Proves the uniform outcome machine, capability reporting, key-reuse classification with the real
///     provider exception preserved, and single-durable-claim convergence under contention.
/// </summary>
public class DynamoConditionalAppendTests
{
    private const string ServiceId = "svc";
    private const string EventsTable = "events";
    private readonly DcbDomainTypes _domain = BuildDomain();

    private static DcbDomainTypes BuildDomain()
    {
        var d = DomainType.GetDomainTypes();
        ((SimpleEventTypes)d.EventTypes).RegisterEventType<MigrationMarker>();
        try
        {
            ((SimpleTagTypes)d.TagTypes).RegisterTagGroupType<MigrationTag>();
        }
        catch (InvalidOperationException)
        {
            // Shared domain instance already has it registered from an earlier test.
        }
        return d;
    }

    private (DynamoDbEventStore Store, FakeDynamoDb Client) NewStore()
    {
        var options = Options.Create(new DynamoDbEventStoreOptions
        {
            EventsTableName = EventsTable,
            TagsTableName = "tags",
            ProjectionStatesTableName = "proj",
            AutoCreateTables = false,
            WriteShardCount = 1
        });
        var client = new FakeDynamoDb();
        var context = new DynamoDbContext(client, options);
        var store = new DynamoDbEventStore(context, _domain.EventTypes, new FixedServiceIdProvider(ServiceId));
        return (store, client);
    }

    private sealed class FixedServiceIdProvider : IServiceIdProvider
    {
        private readonly string _serviceId;
        public FixedServiceIdProvider(string serviceId) => _serviceId = serviceId;
        public string GetCurrentServiceId() => _serviceId;
    }

    private SerializableEvent Marker(string value) =>
        new Event(new MigrationMarker(value), SortableUniqueId.GenerateNew(), nameof(MigrationMarker),
                Guid.CreateVersion7(), new EventMetadata("c", "c", "u"), new List<string> { "Migration:once" })
            .ToSerializableEvent(_domain.EventTypes);

    [Fact]
    public void Capability_ReportsSingleEventUniqueKey()
    {
        var (store, _) = NewStore();
        Assert.True(((IWriteConditionCapabilityProvider)store)
            .DescribeWriteConditions().Supports(WriteConditionKind.SingleEventUniqueKey));
    }

    [Fact]
    public async Task FirstAppend_Wins_SameOperationRetry_ReturnsIdenticalReceipt_NoSecondEvent()
    {
        var (s, client) = NewStore();
        var store = (IConditionalEventStore)s;

        var first = (await store.AppendIfUniqueAsync(new ConditionalAppendRequest("dyn-1", Marker("v")))).GetValue();
        var second = (await store.AppendIfUniqueAsync(new ConditionalAppendRequest("dyn-1", Marker("v")))).GetValue();

        Assert.Equal(ConditionalAppendStatus.Appended, first.Status);
        Assert.Equal(ConditionalAppendStatus.AlreadyCommittedSameOperation, second.Status);
        Assert.Equal(first.WinnerEventId, second.WinnerEventId);
        Assert.Equal(first.WinnerSortableUniqueId, second.WinnerSortableUniqueId);
        Assert.Equal(first.OperationFingerprint, second.OperationFingerprint);
        Assert.Equal(1, client.CountIn(EventsTable));
    }

    [Fact]
    public async Task SameKey_DifferentOperation_IsKeyReuseConflict_WithProviderCause_NoSecondEvent()
    {
        var (s, client) = NewStore();
        var store = (IConditionalEventStore)s;

        Assert.True((await store.AppendIfUniqueAsync(new ConditionalAppendRequest("dyn-2", Marker("first")))).IsSuccess);
        var conflict = await store.AppendIfUniqueAsync(new ConditionalAppendRequest("dyn-2", Marker("DIFFERENT")));

        Assert.False(conflict.IsSuccess);
        var ex = Assert.IsType<KeyReuseConflictException>(conflict.GetException());
        Assert.NotNull(ex.InnerException); // the real TransactionCanceledException is preserved as the diagnostic cause
        Assert.Equal(1, client.CountIn(EventsTable));
    }

    [Fact]
    public async Task NWriters_SameOperation_ConcurrentTransactions_OneAppended_RestAlreadyCommitted_OneDurableEvent()
    {
        var (s, client) = NewStore();
        var store = (IConditionalEventStore)s;

        var attempts = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ =>
                store.AppendIfUniqueAsync(new ConditionalAppendRequest("dyn-race", Marker("payload")))));

        var receipts = attempts.Where(r => r.IsSuccess).Select(r => r.GetValue()).ToList();
        Assert.Equal(10, receipts.Count); // no writer errored
        Assert.Equal(1, receipts.Count(r => r.Status == ConditionalAppendStatus.Appended));
        Assert.Equal(9, receipts.Count(r => r.Status == ConditionalAppendStatus.AlreadyCommittedSameOperation));
        Assert.Single(receipts.Select(r => r.WinnerEventId).Distinct());
        Assert.Single(receipts.Select(r => r.OperationFingerprint).Distinct());
        Assert.Equal(1, client.CountIn(EventsTable));
    }

    /// <summary>
    ///     A thread-safe in-process double for DynamoDB. Only the two operations the conditional path uses are modelled;
    ///     the check-and-apply is serialized under a lock so <c>attribute_not_exists(pk)</c> behaves atomically, exactly as
    ///     DynamoDB's conditional transactional put does.
    /// </summary>
    private sealed class FakeDynamoDb : AmazonDynamoDBClient
    {
        private readonly object _gate = new();
        private readonly Dictionary<(string Table, string Pk, string Sk), Dictionary<string, AttributeValue>> _items = new();

        public FakeDynamoDb() : base(
            new BasicAWSCredentials("fake", "fake"),
            new AmazonDynamoDBConfig { ServiceURL = "http://localhost:8000", AuthenticationRegion = "us-east-1" })
        {
        }

        public int CountIn(string table)
        {
            lock (_gate)
            {
                return _items.Keys.Count(k => k.Table == table);
            }
        }

        public override Task<TransactWriteItemsResponse> TransactWriteItemsAsync(
            TransactWriteItemsRequest request,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var reasons = new List<CancellationReason>();
                var anyFailed = false;
                foreach (var ti in request.TransactItems)
                {
                    var put = ti.Put;
                    var exists = _items.ContainsKey((put.TableName, put.Item["pk"].S, put.Item["sk"].S));
                    if (put.ConditionExpression == "attribute_not_exists(pk)" && exists)
                    {
                        reasons.Add(new CancellationReason { Code = "ConditionalCheckFailed" });
                        anyFailed = true;
                    }
                    else
                    {
                        reasons.Add(new CancellationReason { Code = "None" });
                    }
                }

                if (anyFailed)
                {
                    // All-or-nothing: a single failed condition cancels the whole transaction and applies nothing.
                    throw new TransactionCanceledException("Transaction cancelled") { CancellationReasons = reasons };
                }

                foreach (var ti in request.TransactItems)
                {
                    var put = ti.Put;
                    _items[(put.TableName, put.Item["pk"].S, put.Item["sk"].S)] = put.Item;
                }
            }

            return Task.FromResult(new TransactWriteItemsResponse());
        }

        public override Task<GetItemResponse> GetItemAsync(
            GetItemRequest request,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_items.TryGetValue((request.TableName, request.Key["pk"].S, request.Key["sk"].S), out var item))
                {
                    return Task.FromResult(new GetItemResponse { Item = new Dictionary<string, AttributeValue>(item) });
                }

                return Task.FromResult(new GetItemResponse { Item = new Dictionary<string, AttributeValue>() });
            }
        }
    }

    private record MigrationMarker(string Value) : IEventPayload;

    private record MigrationTag(string Id) : IStringTagGroup<MigrationTag>
    {
        public static string TagGroupName => "Migration";
        public static MigrationTag FromContent(string content) => new(content);
        public bool IsConsistencyTag() => false;
        public string GetId() => Id;
    }
}
