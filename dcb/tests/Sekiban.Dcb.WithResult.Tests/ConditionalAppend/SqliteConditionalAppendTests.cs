using Dcb.Domain;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.TestSupport;
using Xunit;
namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     SEK-G16 SQLite conditional (unique-key) append — the NEW path, exercised in-process against a real temp-file
///     SQLite database (runs in CI with no container). The shared outcome-machine assertions prove the uniform contract;
///     the SQLite-specific coverage is a genuine race between INDEPENDENT store instances / connections against the same
///     real database file (not a single store serialized by one in-process lock), a different-operation race, and the
///     guarantee that the legacy INSERT OR REPLACE path is unchanged.
/// </summary>
public sealed class SqliteConditionalAppendTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"sek-g16-{Guid.NewGuid():N}.db");
    private readonly DcbDomainTypes _domain = ConditionalAppendScenarios.RegisterMarker(DomainType.GetDomainTypes());

    private SqliteEventStore NewStore() => new(_dbPath, _domain.EventTypes);

    private async Task<int> DurableCount(SqliteEventStore store) =>
        (await store.ReadAllSerializableEventsAsync()).GetValue().Count();

    [Fact]
    public void Capability_ReportsSingleEventUniqueKey() =>
        ConditionalAppendScenarios.AssertCapability(NewStore());

    [Fact]
    public async Task FirstAppend_Wins_SameOperationRetry_ReturnsIdenticalReceipt_NoSecondEvent()
    {
        var store = NewStore();
        await ConditionalAppendScenarios.AssertFirstAppendWins_SameOpRetryIsIdempotent(
            store, _domain, "mig-1", () => DurableCount(store));
    }

    [Fact]
    public async Task SameKey_DifferentOperation_IsKeyReuseConflict_WithProviderCause_NoSecondEvent()
    {
        var store = NewStore();
        await ConditionalAppendScenarios.AssertDifferentOperationIsKeyReuseConflict_WithProviderCause(
            store, _domain, "mig-1", () => DurableCount(store));
    }

    [Fact]
    public async Task NWriters_IndependentStores_SameOperation_OneAppended_RestAlreadyCommitted_OneDurableEvent()
    {
        // Each writer is a DISTINCT SqliteEventStore with its own connections against the same real DB file, so the win is
        // arbitrated by SQLite's own write serialization (the loser hits the (ServiceId, Id) PK and rolls back), NOT by a
        // single store's in-process semaphore.
        const int writers = 8;
        var stores = Enumerable.Range(0, writers).Select(_ => NewStore()).ToList();
        var attempts = await Task.WhenAll(
            stores.Select(s => s.AppendIfUniqueAsync(
                new ConditionalAppendRequest("mig-race", ConditionalAppendScenarios.Marker(_domain, "payload")))));

        var receipts = attempts.Where(r => r.IsSuccess).Select(r => r.GetValue()).ToList();
        Assert.Equal(writers, receipts.Count); // no writer errored (losers converge, not fail)
        Assert.Equal(1, receipts.Count(r => r.Status == ConditionalAppendStatus.Appended));
        Assert.Equal(writers - 1, receipts.Count(r => r.Status == ConditionalAppendStatus.AlreadyCommittedSameOperation));
        Assert.Single(receipts.Select(r => r.WinnerEventId).Distinct());
        Assert.Single(receipts.Select(r => r.OperationFingerprint).Distinct());
        Assert.Single((await NewStore().ReadAllSerializableEventsAsync()).GetValue()); // exactly one durable event
    }

    [Fact]
    public async Task IndependentStores_DifferentOperations_SameKey_OneWins_OtherIsKeyReuseConflict()
    {
        // Two independent stores race DIFFERENT operations under the same key. Exactly one durable claim lands; the loser
        // reads the winner back, sees a different fingerprint, and gets a key-reuse conflict — never a second event.
        var a = NewStore();
        var b = NewStore();
        var results = await Task.WhenAll(
            a.AppendIfUniqueAsync(new ConditionalAppendRequest("mig-diff", ConditionalAppendScenarios.Marker(_domain, "A"))),
            b.AppendIfUniqueAsync(new ConditionalAppendRequest("mig-diff", ConditionalAppendScenarios.Marker(_domain, "B"))));

        Assert.Equal(1, results.Count(r => r.IsSuccess && r.GetValue().Status == ConditionalAppendStatus.Appended));
        Assert.Equal(1, results.Count(r => !r.IsSuccess && r.GetException() is KeyReuseConflictException));
        Assert.Single((await NewStore().ReadAllSerializableEventsAsync()).GetValue());
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
            new Event(new ConditionalMarkerEvent(v), sortable, nameof(ConditionalMarkerEvent), id,
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
}
