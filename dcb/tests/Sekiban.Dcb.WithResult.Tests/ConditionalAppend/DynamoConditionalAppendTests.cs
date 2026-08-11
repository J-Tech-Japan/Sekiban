using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Dcb.Domain;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.TestSupport;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     SEK-G16 DynamoDB conditional (unique-key) append through the real <see cref="DynamoDbEventStore" /> against an
///     in-process, thread-safe fake <see cref="IAmazonDynamoDB" /> whose atomic conditional put is serialized under a lock
///     (a genuine concurrent-writer race). Beyond the shared uniform contract, the Dynamo-specific coverage is: the
///     transaction limit/duplicate-item fail-closed guards before the network, cancellation-reason-INDEX discrimination
///     (only the event Put at index 0 is the claim), ambiguous commit after a durable write resolving by read-back, and a
///     bare conflict with no readable winner surfacing typed retryable in-doubt.
/// </summary>
public class DynamoConditionalAppendTests
{
    private const string ServiceId = "svc";
    private const string EventsTable = "events";
    private readonly DcbDomainTypes _domain = BuildDomain();

    private static DcbDomainTypes BuildDomain()
    {
        var d = ConditionalAppendScenarios.RegisterMarker(DomainType.GetDomainTypes());
        ((SimpleEventTypes)d.EventTypes).RegisterEventType<ManyTagMarker>();
        try
        {
            ((SimpleTagTypes)d.TagTypes).RegisterTagGroupType<ManyTag>();
        }
        catch (InvalidOperationException)
        {
            // Already registered on the shared domain instance by an earlier test.
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

    private SerializableEvent MarkerWithTags(string value, IEnumerable<string> tags) =>
        new Event(new ManyTagMarker(value), SortableUniqueId.GenerateNew(), nameof(ManyTagMarker),
                Guid.CreateVersion7(), new EventMetadata("c", "c", "u"), tags.ToList())
            .ToSerializableEvent(_domain.EventTypes);

    // ── Shared uniform contract ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RealStoreHeadSeedsMonotonicGeneratorAbovePersistedMaximum()
    {
        var (store, _) = NewStore();
        var persistedTicks = new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc).Ticks;
        var persisted = new Event(
                new ManyTagMarker("seed"),
                SortableUniqueId.Generate(new DateTime(persistedTicks, DateTimeKind.Utc), Guid.NewGuid()),
                nameof(ManyTagMarker),
                Guid.CreateVersion7(),
                new EventMetadata("seed", "seed", "test"),
                ["Many:seed"])
            .ToSerializableEvent(_domain.EventTypes);
        Assert.True((await store.WriteSerializableEventsAsync([persisted])).IsSuccess);

        var generator = new MonotonicSortableUniqueIdGenerator(
            new FixedTimeProvider(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        await new SortableUniqueIdSeedCoordinator(generator).EnsureSeededAsync(ServiceId, store);

        Assert.True(string.CompareOrdinal(generator.GenerateNew(), persisted.SortableUniqueIdValue) > 0);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
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

    // ── Transaction limits: fail-closed BEFORE the network ──────────────────────────────────────────

    [Fact]
    public async Task NinetyNineTags_AtTheLimit_Appends()
    {
        var (s, client) = NewStore();
        var store = (IConditionalEventStore)s;
        var tags = Enumerable.Range(0, 99).Select(i => $"Many:{i}");
        var result = await store.AppendIfUniqueAsync(new ConditionalAppendRequest("dyn-99", MarkerWithTags("v", tags)));

        Assert.True(result.IsSuccess); // 1 event + 99 tags = 100 items, exactly the TransactWriteItems cap
        Assert.Equal(1, client.CountIn(EventsTable));
    }

    [Fact]
    public async Task OneHundredTags_OverTheLimit_FailsClosed_BeforeAnyCall()
    {
        var (s, client) = NewStore();
        var store = (IConditionalEventStore)s;
        var tags = Enumerable.Range(0, 100).Select(i => $"Many:{i}");
        var result = await store.AppendIfUniqueAsync(new ConditionalAppendRequest("dyn-100", MarkerWithTags("v", tags)));

        Assert.False(result.IsSuccess); // 1 event + 100 tags = 101 items > 100
        Assert.IsType<DynamoConditionalAppendLimitException>(result.GetException());
        Assert.Equal(0, client.TransactCalls); // never reached the network
    }

    [Fact]
    public async Task DuplicateTagItems_FailClosed_BeforeAnyCall()
    {
        var (s, client) = NewStore();
        var store = (IConditionalEventStore)s;
        var result = await store.AppendIfUniqueAsync(
            new ConditionalAppendRequest("dyn-dup", MarkerWithTags("v", new[] { "Many:same", "Many:same" })));

        Assert.False(result.IsSuccess); // duplicate item key in one transaction
        Assert.IsType<DynamoConditionalAppendLimitException>(result.GetException());
        Assert.Equal(0, client.TransactCalls);
    }

    // ── Cancellation-reason-index discrimination ────────────────────────────────────────────────────

    [Fact]
    public async Task CancellationReason_AtNonEventIndex_IsProviderFailure_NotAClaimConflict()
    {
        var (s, client) = NewStore();
        var store = (IConditionalEventStore)s;
        // Event Put (index 0) is fine; a tag Put (index 1) is cancelled for an unrelated reason. This is NOT the claim
        // condition and must preserve its original failure, never becoming a winner classification.
        client.NextTransactException = new TransactionCanceledException("cancelled")
        {
            CancellationReasons = new List<CancellationReason>
            {
                new() { Code = "None" },
                new() { Code = "ValidationError" }
            }
        };

        var result = await store.AppendIfUniqueAsync(new ConditionalAppendRequest("dyn-idx", ConditionalAppendScenarios.Marker(_domain, "v")));

        Assert.False(result.IsSuccess);
        Assert.IsType<TransactionCanceledException>(result.GetException());
    }

    // ── Ambiguous commit after a durable write ──────────────────────────────────────────────────────

    [Fact]
    public async Task PreDispatchCancellation_IsRawOriginalCancellation_ZeroDispatch_NoDurableState_NotInDoubt()
    {
        var (s, client) = NewStore();
        var store = (IConditionalEventStore)s;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // An already-cancelled token is a KNOWN no-commit: the pre-dispatch boundary throws before any SDK call, so it
        // surfaces the exact original OperationCanceledException/token — never the post-commit ambiguity marker/in-doubt.
        var result = await store.AppendIfUniqueAsync(
            new ConditionalAppendRequest("dyn-precancel", ConditionalAppendScenarios.Marker(_domain, "v")), cts.Token);

        Assert.False(result.IsSuccess);
        Assert.IsNotType<ConditionalAppendInDoubtException>(result.GetException());
        var oce = Assert.IsAssignableFrom<OperationCanceledException>(result.GetException());
        Assert.Equal(cts.Token, oce.CancellationToken);
        Assert.Equal(0, client.TransactCalls);         // zero SDK dispatch
        Assert.Equal(0, client.CountIn(EventsTable));  // no durable state
    }

    [Fact]
    public async Task AmbiguousCancellation_AfterDurableCommit_ResolvesByReadback_ToAlreadyCommitted()
    {
        var (s, client) = NewStore();
        var store = (IConditionalEventStore)s;
        // The transaction applies durably, then the call surfaces a cancellation (ambiguous to the caller). The
        // orchestrator must resolve it by authoritative read-back + fingerprint, not fail blindly.
        client.ApplyThenThrowOnce = new OperationCanceledException("ambiguous after commit");

        var result = await store.AppendIfUniqueAsync(new ConditionalAppendRequest("dyn-amb", ConditionalAppendScenarios.Marker(_domain, "v")));

        Assert.True(result.IsSuccess);
        Assert.Equal(ConditionalAppendStatus.AlreadyCommittedSameOperation, result.GetValue().Status);
        Assert.Equal(1, client.CountIn(EventsTable));
    }

    [Fact]
    public async Task AbsentCancellationReasons_IsProviderFailure_NotAClaimConflict()
    {
        var (s, client) = NewStore();
        var store = (IConditionalEventStore)s;
        // A TransactionCanceledException with an EMPTY reason collection is not the event claim condition.
        client.NextTransactException = new TransactionCanceledException("cancelled") { CancellationReasons = new List<CancellationReason>() };

        var result = await store.AppendIfUniqueAsync(new ConditionalAppendRequest("dyn-absent", ConditionalAppendScenarios.Marker(_domain, "v")));

        Assert.False(result.IsSuccess);
        Assert.IsType<TransactionCanceledException>(result.GetException());
    }

    [Fact]
    public async Task MalformedCancellationReason_AtIndex0_ThatIsNotConditionalCheck_IsProviderFailure()
    {
        var (s, client) = NewStore();
        var store = (IConditionalEventStore)s;
        // Index 0 is present but is a throttling reason, not ConditionalCheckFailed — a provider failure, not a conflict.
        client.NextTransactException = new TransactionCanceledException("cancelled")
        {
            CancellationReasons = new List<CancellationReason> { new() { Code = "ThrottlingError" } }
        };

        var result = await store.AppendIfUniqueAsync(new ConditionalAppendRequest("dyn-malformed", ConditionalAppendScenarios.Marker(_domain, "v")));

        Assert.False(result.IsSuccess);
        Assert.IsType<TransactionCanceledException>(result.GetException());
    }

    [Fact]
    public async Task ProviderRejection_RequestTooLarge_IsProviderFailure_NotInDoubtOrConflict()
    {
        var (s, client) = NewStore();
        var store = (IConditionalEventStore)s;
        client.NextTransactException = new AmazonDynamoDBException("Transaction request cannot include more than allowed size");

        var result = await store.AppendIfUniqueAsync(new ConditionalAppendRequest("dyn-toolarge", ConditionalAppendScenarios.Marker(_domain, "v")));

        Assert.False(result.IsSuccess);
        Assert.IsType<AmazonDynamoDBException>(result.GetException());
        Assert.IsNotType<ConditionalAppendInDoubtException>(result.GetException());
    }

    [Fact]
    public async Task BareConflict_WithNoReadableWinner_IsTypedRetryableInDoubt()
    {
        var (s, client) = NewStore();
        var store = (IConditionalEventStore)s;
        // A conditional-check failure is signalled, but the item does not actually exist to read back — in-doubt.
        client.NextTransactException = new TransactionCanceledException("cancelled")
        {
            CancellationReasons = new List<CancellationReason> { new() { Code = "ConditionalCheckFailed" } }
        };

        var result = await store.AppendIfUniqueAsync(new ConditionalAppendRequest("dyn-indoubt", ConditionalAppendScenarios.Marker(_domain, "v")));

        Assert.False(result.IsSuccess);
        var ex = Assert.IsType<ConditionalAppendInDoubtException>(result.GetException());
        Assert.True(ex.IsRetryable);
        Assert.Equal(0, client.CountIn(EventsTable));
    }

    /// <summary>
    ///     Thread-safe DynamoDB double. Models the atomic conditional put, plus seams for an injected transaction
    ///     exception (without applying) and an apply-then-throw ambiguous commit.
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

        /// <summary>Thrown on the next transaction WITHOUT applying it (models a rejection / crash before commit).</summary>
        public Exception? NextTransactException { get; set; }

        /// <summary>Applied durably, THEN thrown once (models an ambiguous cancellation after a durable commit).</summary>
        public Exception? ApplyThenThrowOnce { get; set; }

        public int TransactCalls { get; private set; }

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
                TransactCalls++;

                if (NextTransactException is { } injected)
                {
                    NextTransactException = null;
                    throw injected;
                }

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
                    throw new TransactionCanceledException("Transaction cancelled") { CancellationReasons = reasons };
                }

                foreach (var ti in request.TransactItems)
                {
                    var put = ti.Put;
                    _items[(put.TableName, put.Item["pk"].S, put.Item["sk"].S)] = put.Item;
                }

                if (ApplyThenThrowOnce is { } ambiguous)
                {
                    ApplyThenThrowOnce = null;
                    throw ambiguous;
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

        public override Task<QueryResponse> QueryAsync(
            QueryRequest request,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var partition = request.ExpressionAttributeValues[":pk"].S;
                var items = _items
                    .Where(entry => entry.Key.Table == request.TableName)
                    .Select(entry => entry.Value)
                    .Where(item => item.TryGetValue("gsi1pk", out var value) && value.S == partition)
                    .OrderByDescending(item => item["sortableUniqueId"].S, StringComparer.Ordinal)
                    .Take(request.Limit ?? int.MaxValue)
                    .Select(item => new Dictionary<string, AttributeValue>(item))
                    .ToList();
                return Task.FromResult(new QueryResponse { Items = items });
            }
        }
    }

    private record ManyTagMarker(string Value) : IEventPayload;

    private record ManyTag(string Id) : IStringTagGroup<ManyTag>
    {
        public static string TagGroupName => "Many";
        public static ManyTag FromContent(string content) => new(content);
        public bool IsConsistencyTag() => false;
        public string GetId() => Id;
    }
}
