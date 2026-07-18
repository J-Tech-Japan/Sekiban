using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Xunit;
namespace Sekiban.Dcb.Postgres.Tests.ConditionalAppend;

/// <summary>
///     SEK-G16 Postgres conditional (unique-key) append against a real PostgreSQL container (Testcontainers). Proves the
///     uniform outcome machine and — with genuine cross-transaction concurrency — an N-writer race converging on one
///     durable winner via the primary-key/23505 primitive.
/// </summary>
public class PostgresConditionalAppendTests : PostgresTestBase
{
    public PostgresConditionalAppendTests(PostgresTestFixture fixture) : base(fixture)
    {
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        // Register the marker types into the fixture's shared domain so the fingerprint can resolve them. The domain is a
        // shared collection fixture, so registration runs once per test — event-type registration is idempotent, but tag
        // registration throws on a duplicate, so guard it.
        ((SimpleEventTypes)Fixture.DomainTypes.EventTypes).RegisterEventType<MigrationMarker>();
        try
        {
            ((SimpleTagTypes)Fixture.DomainTypes.TagTypes).RegisterTagGroupType<MigrationTag>();
        }
        catch (InvalidOperationException)
        {
            // Already registered by an earlier test sharing the fixture domain.
        }
    }

    private IConditionalEventStore Conditional => (IConditionalEventStore)Fixture.EventStore;

    private SerializableEvent Marker(string value) =>
        new Event(new MigrationMarker(value), SortableUniqueId.GenerateNew(), nameof(MigrationMarker),
                Guid.CreateVersion7(), new EventMetadata("c", "c", "u"), new List<string> { "Migration:once" })
            .ToSerializableEvent(Fixture.DomainTypes.EventTypes);

    [Fact]
    public void Capability_ReportsSingleEventUniqueKey() =>
        Assert.True(((IWriteConditionCapabilityProvider)Fixture.EventStore)
            .DescribeWriteConditions().Supports(WriteConditionKind.SingleEventUniqueKey));

    [Fact]
    public async Task FirstAppend_Wins_SameOperationRetry_ReturnsIdenticalReceipt_NoSecondEvent()
    {
        var first = (await Conditional.AppendIfUniqueAsync(new ConditionalAppendRequest("pg-1", Marker("v")))).GetValue();
        var second = (await Conditional.AppendIfUniqueAsync(new ConditionalAppendRequest("pg-1", Marker("v")))).GetValue();

        Assert.Equal(ConditionalAppendStatus.Appended, first.Status);
        Assert.Equal(ConditionalAppendStatus.AlreadyCommittedSameOperation, second.Status);
        Assert.Equal(first.WinnerEventId, second.WinnerEventId);
        Assert.Equal(first.WinnerSortableUniqueId, second.WinnerSortableUniqueId);
        Assert.Equal(first.OperationFingerprint, second.OperationFingerprint);
        Assert.Single((await Fixture.EventStore.ReadAllSerializableEventsAsync()).GetValue());
    }

    [Fact]
    public async Task SameKey_DifferentOperation_IsKeyReuseConflict_WithProviderCause()
    {
        Assert.True((await Conditional.AppendIfUniqueAsync(new ConditionalAppendRequest("pg-2", Marker("first")))).IsSuccess);
        var conflict = await Conditional.AppendIfUniqueAsync(new ConditionalAppendRequest("pg-2", Marker("DIFFERENT")));

        Assert.False(conflict.IsSuccess);
        var ex = Assert.IsType<KeyReuseConflictException>(conflict.GetException());
        Assert.NotNull(ex.InnerException); // 23505 preserved as the diagnostic cause
        Assert.Single((await Fixture.EventStore.ReadAllSerializableEventsAsync()).GetValue());
    }

    [Fact]
    public async Task NWriters_SameOperation_ConcurrentTransactions_OneAppended_RestAlreadyCommitted_OneDurableEvent()
    {
        var attempts = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ =>
                Conditional.AppendIfUniqueAsync(new ConditionalAppendRequest("pg-race", Marker("payload")))));

        var receipts = attempts.Where(r => r.IsSuccess).Select(r => r.GetValue()).ToList();
        Assert.Equal(10, receipts.Count); // no writer errored
        Assert.Equal(1, receipts.Count(r => r.Status == ConditionalAppendStatus.Appended));
        Assert.Equal(9, receipts.Count(r => r.Status == ConditionalAppendStatus.AlreadyCommittedSameOperation));
        Assert.Single(receipts.Select(r => r.WinnerEventId).Distinct());
        Assert.Single(receipts.Select(r => r.OperationFingerprint).Distinct());
        Assert.Single((await Fixture.EventStore.ReadAllSerializableEventsAsync()).GetValue());
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
