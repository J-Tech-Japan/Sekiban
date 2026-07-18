using Dcb.Domain;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.TestSupport;
using Sekiban.Dcb.Tests.Cosmos;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     SEK-G16 Cosmos DB conditional (unique-key) append, driven end to end through the real
///     <see cref="CosmosDbEventStore" /> against in-memory Cosmos containers. The shared outcome-machine assertions
///     (<see cref="ConditionalAppendScenarios" />) prove the uniform contract; the Cosmos-specific case is the point of
///     the read-back design — a bare 409 is NEVER on its own a same-operation success: with no committed winner to verify
///     against, the append is in-doubt, not AlreadyCommitted.
///     Real N-writer concurrency is proven by the database-backed providers (Postgres, SQLite); the in-memory Cosmos
///     double is a deterministic single-threaded fault harness, so convergence here is exercised by repeated append.
/// </summary>
public class CosmosConditionalAppendTests
{
    private const string ServiceId = "svc";
    private readonly DcbDomainTypes _domain = ConditionalAppendScenarios.RegisterMarker(DomainType.GetDomainTypes());

    private sealed class Lineage
    {
        public Lineage(DcbDomainTypes domain)
        {
            var options = new CosmosDbEventStoreOptions { EventsContainerName = "events", TagsContainerName = "tags" };
            Client = new InMemoryCosmosClient();
            Options = options;
            var context = new CosmosDbContext(Client, "test-db", null, options);
            var resolver = new DefaultCosmosContainerResolver(options);
            Store = new CosmosDbEventStore(context, domain.EventTypes, new FixedServiceIdProvider(ServiceId), resolver);
        }

        public InMemoryCosmosClient Client { get; }
        public CosmosDbEventStoreOptions Options { get; }
        public CosmosDbEventStore Store { get; }
        public InMemoryCosmosContainer Events => Client.Container(Options.EventsContainerName);
        public Task<int> DurableCount() => Task.FromResult(Events.Items.Count);
    }

    private sealed class FixedServiceIdProvider : IServiceIdProvider
    {
        private readonly string _serviceId;
        public FixedServiceIdProvider(string serviceId) => _serviceId = serviceId;
        public string GetCurrentServiceId() => _serviceId;
    }

    [Fact]
    public void Capability_ReportsSingleEventUniqueKey() =>
        ConditionalAppendScenarios.AssertCapability(new Lineage(_domain).Store);

    [Fact]
    public Task FirstAppend_Wins_SameOperationRetry_ReturnsIdenticalReceipt_NoSecondEvent()
    {
        var lineage = new Lineage(_domain);
        return ConditionalAppendScenarios.AssertFirstAppendWins_SameOpRetryIsIdempotent(
            lineage.Store, _domain, "cosmos-1", lineage.DurableCount);
    }

    [Fact]
    public Task SameKey_DifferentOperation_IsKeyReuseConflict_WithProviderCause_NoSecondEvent()
    {
        var lineage = new Lineage(_domain);
        return ConditionalAppendScenarios.AssertDifferentOperationIsKeyReuseConflict_WithProviderCause(
            lineage.Store, _domain, "cosmos-2", lineage.DurableCount);
    }

    [Fact]
    public async Task Bare409_WithoutACommittedWinner_IsInDoubt_NotAlreadyCommitted()
    {
        // A conflict alone is never proof of a same-operation success. Inject a 409 on the event create while NO winner
        // actually exists to read back: the append must fail in-doubt (retryable), never report AlreadyCommitted.
        var lineage = new Lineage(_domain);
        var store = (IConditionalEventStore)lineage.Store;
        lineage.Events.WriteFaults.Enqueue(CosmosFailures.Conflict());

        var result = await store.AppendIfUniqueAsync(
            new ConditionalAppendRequest("cosmos-indoubt", ConditionalAppendScenarios.Marker(_domain, "v")));

        Assert.False(result.IsSuccess);
        Assert.IsType<InvalidOperationException>(result.GetException());
        Assert.Empty(lineage.Events.Items); // nothing committed
    }

    [Fact]
    public async Task RepeatedAppend_SameOperation_ConvergesOnOneDurableEvent()
    {
        // The in-memory double is single-threaded, so convergence is exercised by repeated (sequential) append.
        var lineage = new Lineage(_domain);
        var store = (IConditionalEventStore)lineage.Store;

        var receipts = new List<ConditionalAppendReceipt>();
        for (var i = 0; i < 5; i++)
        {
            receipts.Add((await store.AppendIfUniqueAsync(
                new ConditionalAppendRequest("cosmos-conv", ConditionalAppendScenarios.Marker(_domain, "payload")))).GetValue());
        }

        Assert.Equal(1, receipts.Count(r => r.Status == ConditionalAppendStatus.Appended));
        Assert.Equal(4, receipts.Count(r => r.Status == ConditionalAppendStatus.AlreadyCommittedSameOperation));
        Assert.Single(receipts.Select(r => r.WinnerEventId).Distinct());
        Assert.Single(receipts.Select(r => r.OperationFingerprint).Distinct());
        Assert.Single(lineage.Events.Items);
    }
}
