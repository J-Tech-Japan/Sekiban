using Dcb.Domain;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     SEK-G16 SQLite conditional (unique-key) append — the NEW path, exercised in-process against a real temp-file
///     SQLite database (runs in CI with no container). Proves the uniform outcome machine, an N-writer race converging on
///     one durable winner, capability reporting, and that the legacy INSERT OR REPLACE path is unchanged.
/// </summary>
public sealed class SqliteConditionalAppendTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"sek-g16-{Guid.NewGuid():N}.db");
    private readonly DcbDomainTypes _domain = BuildDomain();

    private static DcbDomainTypes BuildDomain()
    {
        var d = DomainType.GetDomainTypes();
        ((SimpleEventTypes)d.EventTypes).RegisterEventType<MigrationMarker>();
        ((SimpleTagTypes)d.TagTypes).RegisterTagGroupType<MigrationTag>();
        return d;
    }

    private SqliteEventStore NewStore() => new(_dbPath, _domain.EventTypes);

    private SerializableEvent Marker(string value) =>
        new Event(new MigrationMarker(value), SortableUniqueId.GenerateNew(), nameof(MigrationMarker),
                Guid.CreateVersion7(), new EventMetadata("c", "c", "u"), new List<string> { "Migration:once" })
            .ToSerializableEvent(_domain.EventTypes);

    [Fact]
    public void Capability_ReportsSingleEventUniqueKey()
    {
        Assert.True(NewStore().DescribeWriteConditions().Supports(WriteConditionKind.SingleEventUniqueKey));
    }

    [Fact]
    public async Task FirstAppend_Wins_SameOperationRetry_ReturnsIdenticalReceipt_NoSecondEvent()
    {
        var store = NewStore();
        var first = (await store.AppendIfUniqueAsync(new ConditionalAppendRequest("mig-1", Marker("v")))).GetValue();
        var second = (await store.AppendIfUniqueAsync(new ConditionalAppendRequest("mig-1", Marker("v")))).GetValue();

        Assert.Equal(ConditionalAppendStatus.Appended, first.Status);
        Assert.Equal(ConditionalAppendStatus.AlreadyCommittedSameOperation, second.Status);
        Assert.Equal(first.WinnerEventId, second.WinnerEventId);
        Assert.Equal(first.WinnerSortableUniqueId, second.WinnerSortableUniqueId);
        Assert.Equal(first.OperationFingerprint, second.OperationFingerprint);
        Assert.Single((await store.ReadAllSerializableEventsAsync()).GetValue());
    }

    [Fact]
    public async Task SameKey_DifferentOperation_IsKeyReuseConflict_WithProviderCause_NoSecondEvent()
    {
        var store = NewStore();
        Assert.True((await store.AppendIfUniqueAsync(new ConditionalAppendRequest("mig-1", Marker("first")))).IsSuccess);
        var conflict = await store.AppendIfUniqueAsync(new ConditionalAppendRequest("mig-1", Marker("DIFFERENT")));

        Assert.False(conflict.IsSuccess);
        var ex = Assert.IsType<KeyReuseConflictException>(conflict.GetException());
        Assert.NotNull(ex.InnerException); // constraint-discovered conflict preserves the real SQLite exception
        Assert.Single((await store.ReadAllSerializableEventsAsync()).GetValue());
    }

    [Fact]
    public async Task NWriters_SameOperation_OneAppended_RestAlreadyCommitted_IdenticalReceipt_OneDurableEvent()
    {
        var store = NewStore();
        var attempts = await Task.WhenAll(
            Enumerable.Range(0, 12).Select(_ =>
                store.AppendIfUniqueAsync(new ConditionalAppendRequest("mig-race", Marker("payload")))));

        var receipts = attempts.Select(r => r.GetValue()).ToList();
        Assert.Equal(1, receipts.Count(r => r.Status == ConditionalAppendStatus.Appended));
        Assert.Equal(11, receipts.Count(r => r.Status == ConditionalAppendStatus.AlreadyCommittedSameOperation));
        Assert.Single(receipts.Select(r => r.WinnerEventId).Distinct());          // all converge on ONE winner id
        Assert.Single(receipts.Select(r => r.WinnerSortableUniqueId).Distinct());
        Assert.Single(receipts.Select(r => r.OperationFingerprint).Distinct());
        Assert.Single((await store.ReadAllSerializableEventsAsync()).GetValue()); // exactly one durable event
    }

    [Fact]
    public async Task LegacyInsertOrReplacePath_IsUnchanged_SecondWriteOfSameIdOverwrites()
    {
        // Regression pin: the unconditional path still uses INSERT OR REPLACE (upsert by (ServiceId, Id)), NOT the
        // conditional reject behaviour. Writing the same EventId twice overwrites and keeps a single row.
        var store = NewStore();
        var id = Guid.CreateVersion7();
        var sortable = SortableUniqueId.GenerateNew();
        SerializableEvent Fixed(string v) =>
            new Event(new MigrationMarker(v), sortable, nameof(MigrationMarker), id,
                    new EventMetadata("c", "c", "u"), new List<string>())
                .ToSerializableEvent(_domain.EventTypes);

        Assert.True((await store.WriteSerializableEventsAsync(new[] { Fixed("a") })).IsSuccess);
        Assert.True((await store.WriteSerializableEventsAsync(new[] { Fixed("b") })).IsSuccess); // no throw: OR REPLACE
        Assert.Single((await store.ReadAllSerializableEventsAsync()).GetValue());
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // best-effort temp cleanup
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
