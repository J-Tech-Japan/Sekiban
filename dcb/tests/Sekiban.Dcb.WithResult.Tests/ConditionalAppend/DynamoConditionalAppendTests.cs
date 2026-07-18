using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Dcb.Domain;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.TestSupport;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     SEK-G16 DynamoDB conditional (unique-key) append, driven end to end through the real
///     <see cref="DynamoDbEventStore" /> against an in-process, thread-safe fake <see cref="IAmazonDynamoDB" /> whose
///     check-and-apply is serialized under a lock, reproducing DynamoDB's atomic conditional put. The shared
///     outcome-machine assertions (<see cref="ConditionalAppendScenarios" />) prove the uniform contract; because the fake
///     is atomic, the N-writer case is a genuine concurrent race converging on one durable claim via
///     <c>attribute_not_exists(pk)</c>.
/// </summary>
public class DynamoConditionalAppendTests
{
    private const string ServiceId = "svc";
    private const string EventsTable = "events";
    private readonly DcbDomainTypes _domain = ConditionalAppendScenarios.RegisterMarker(DomainType.GetDomainTypes());

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

    [Fact]
    public void Capability_ReportsSingleEventUniqueKey() =>
        ConditionalAppendScenarios.AssertCapability(NewStore().Store);

    [Fact]
    public Task FirstAppend_Wins_SameOperationRetry_ReturnsIdenticalReceipt_NoSecondEvent()
    {
        var (store, client) = NewStore();
        return ConditionalAppendScenarios.AssertFirstAppendWins_SameOpRetryIsIdempotent(
            store, _domain, "dyn-1", () => Task.FromResult(client.CountIn(EventsTable)));
    }

    [Fact]
    public Task SameKey_DifferentOperation_IsKeyReuseConflict_WithProviderCause_NoSecondEvent()
    {
        var (store, client) = NewStore();
        return ConditionalAppendScenarios.AssertDifferentOperationIsKeyReuseConflict_WithProviderCause(
            store, _domain, "dyn-2", () => Task.FromResult(client.CountIn(EventsTable)));
    }

    [Fact]
    public Task NWriters_SameOperation_ConcurrentTransactions_OneAppended_RestAlreadyCommitted_OneDurableEvent()
    {
        var (store, client) = NewStore();
        return ConditionalAppendScenarios.AssertNWritersConverge(
            store, _domain, "dyn-race", 10, () => Task.FromResult(client.CountIn(EventsTable)));
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
}
