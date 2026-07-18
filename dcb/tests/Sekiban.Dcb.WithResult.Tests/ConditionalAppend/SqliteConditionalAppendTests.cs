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
///     SQLite database (runs in CI with no container). The shared outcome-machine assertions
///     (<see cref="ConditionalAppendScenarios" />) prove the uniform contract; the SQLite-specific cases here are the
///     N-writer race under the write lock and the guarantee that the legacy INSERT OR REPLACE path is unchanged.
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
    public async Task NWriters_SameOperation_OneAppended_RestAlreadyCommitted_IdenticalReceipt_OneDurableEvent()
    {
        var store = NewStore();
        await ConditionalAppendScenarios.AssertNWritersConverge(store, _domain, "mig-race", 12, () => DurableCount(store));
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
