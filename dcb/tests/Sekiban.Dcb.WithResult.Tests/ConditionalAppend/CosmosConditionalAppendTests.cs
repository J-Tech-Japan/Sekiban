using Dcb.Domain;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Tests.Cosmos;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     SEK-G16 Cosmos DB conditional (unique-key) append, driven end to end through the real
///     <see cref="CosmosDbEventStore" /> against in-memory Cosmos containers. The claim event is created under the
///     deterministic id, so per-item uniqueness is the primitive (no schema change). Proves the uniform outcome machine,
///     capability reporting, and — the point of the Cosmos read-back design — that a bare 409 is NEVER on its own treated
///     as a same-operation success: without a committed winner to verify against, the append is in-doubt, not
///     AlreadyCommitted.
///     Real N-writer concurrency is proven by the database-backed providers (Postgres, SQLite); the in-memory Cosmos
///     double is a deterministic single-threaded fault harness, so convergence here is exercised by repeated append.
/// </summary>
public class CosmosConditionalAppendTests
{
    private const string ServiceId = "svc";
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
        var store = new Lineage(_domain).Store;
        Assert.True(((IWriteConditionCapabilityProvider)store)
            .DescribeWriteConditions().Supports(WriteConditionKind.SingleEventUniqueKey));
    }

    [Fact]
    public async Task FirstAppend_Wins_SameOperationRetry_ReturnsIdenticalReceipt_NoSecondEvent()
    {
        var lineage = new Lineage(_domain);
        var store = (IConditionalEventStore)lineage.Store;

        var first = (await store.AppendIfUniqueAsync(new ConditionalAppendRequest("cosmos-1", Marker("v")))).GetValue();
        var second = (await store.AppendIfUniqueAsync(new ConditionalAppendRequest("cosmos-1", Marker("v")))).GetValue();

        Assert.Equal(ConditionalAppendStatus.Appended, first.Status);
        Assert.Equal(ConditionalAppendStatus.AlreadyCommittedSameOperation, second.Status);
        Assert.Equal(first.WinnerEventId, second.WinnerEventId);
        Assert.Equal(first.WinnerSortableUniqueId, second.WinnerSortableUniqueId);
        Assert.Equal(first.OperationFingerprint, second.OperationFingerprint);
        Assert.Single(lineage.Events.Items);
    }

    [Fact]
    public async Task SameKey_DifferentOperation_IsKeyReuseConflict_WithProviderCause_NoSecondEvent()
    {
        var lineage = new Lineage(_domain);
        var store = (IConditionalEventStore)lineage.Store;

        Assert.True((await store.AppendIfUniqueAsync(new ConditionalAppendRequest("cosmos-2", Marker("first")))).IsSuccess);
        var conflict = await store.AppendIfUniqueAsync(new ConditionalAppendRequest("cosmos-2", Marker("DIFFERENT")));

        Assert.False(conflict.IsSuccess);
        var ex = Assert.IsType<KeyReuseConflictException>(conflict.GetException());
        Assert.NotNull(ex.InnerException); // the real Cosmos 409 is preserved as the diagnostic cause
        Assert.Single(lineage.Events.Items);
    }

    [Fact]
    public async Task Bare409_WithoutACommittedWinner_IsInDoubt_NotAlreadyCommitted()
    {
        // A conflict alone is never proof of a same-operation success. Inject a 409 on the event create while NO winner
        // actually exists to read back: the append must fail in-doubt (retryable), never report AlreadyCommitted.
        var lineage = new Lineage(_domain);
        var store = (IConditionalEventStore)lineage.Store;
        lineage.Events.WriteFaults.Enqueue(CosmosFailures.Conflict());

        var result = await store.AppendIfUniqueAsync(new ConditionalAppendRequest("cosmos-indoubt", Marker("v")));

        Assert.False(result.IsSuccess);
        Assert.IsType<InvalidOperationException>(result.GetException());
        Assert.Empty(lineage.Events.Items); // nothing committed
    }

    [Fact]
    public async Task RepeatedAppend_SameOperation_ConvergesOnOneDurableEvent()
    {
        var lineage = new Lineage(_domain);
        var store = (IConditionalEventStore)lineage.Store;

        var receipts = new List<ConditionalAppendReceipt>();
        for (var i = 0; i < 5; i++)
        {
            receipts.Add((await store.AppendIfUniqueAsync(new ConditionalAppendRequest("cosmos-conv", Marker("payload")))).GetValue());
        }

        Assert.Equal(1, receipts.Count(r => r.Status == ConditionalAppendStatus.Appended));
        Assert.Equal(4, receipts.Count(r => r.Status == ConditionalAppendStatus.AlreadyCommittedSameOperation));
        Assert.Single(receipts.Select(r => r.WinnerEventId).Distinct());
        Assert.Single(receipts.Select(r => r.OperationFingerprint).Distinct());
        Assert.Single(lineage.Events.Items);
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
