using System.Text;
using System.Text.Json;
using Dcb.Domain;
using Dcb.Domain.Student;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Xunit;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     SEK-G18 (#1086): the catch-up read after a restore starts from <c>record.LastSortableUniqueId</c> and MUST be
///     exclusive of that position — an event whose SortableUniqueId equals the checkpoint position is already reflected in
///     the restored payload and must NOT be re-read (which would double-count / re-fold). This pins the
///     exclusive-after-position boundary with LITERAL at-position vectors for the in-memory core store (the store the
///     projection catch-up uses in tests) and SQLite. Postgres pins the same boundary vector against a REAL store in its
///     Testcontainers-backed provider test project (PostgresExclusiveAfterPositionTests, which runs in CI). The Cosmos
///     ("c.sortableUniqueId &gt; @since"), DynamoDB ("sortableUniqueId &gt; :since") and Hybrid (delegates to the hot/cold
///     stores) providers use the identical strictly-greater-than filter — verified by inspection of each ReadAllEventsAsync;
///     they have no in-repo emulator test project, so Postgres is the runnable cross-provider proof.
/// </summary>
public class ExclusiveAfterPositionTests
{
    // Three ordinally-sortable SortableUniqueId positions with valid tick prefixes (distinct timestamps → deterministic
    // ordering). P2 is the "at position" boundary vector: a read since=P2 must exclude P1 (<) and P2 (==), yield only P3.
    private static readonly DateTime BaseTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string P1 = SortableUniqueId.Generate(BaseTime, Guid.Empty);
    private static readonly string P2 = SortableUniqueId.Generate(BaseTime.AddSeconds(1), Guid.Empty);
    private static readonly string P3 = SortableUniqueId.Generate(BaseTime.AddSeconds(2), Guid.Empty);

    private static readonly DcbDomainTypes Domain = DomainType.GetDomainTypes();

    private static SerializableEvent Ev(string sortableId) => new(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new StudentCreated(Guid.NewGuid(), "n", 5), Domain.JsonSerializerOptions)),
        sortableId,
        Guid.NewGuid(),
        new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
        new List<string>(),
        nameof(StudentCreated));

    private static IEventTypes EventTypes => Domain.EventTypes;

    [Fact]
    public async Task InMemoryStore_ReadAfterPosition_ExcludesTheEventAtThePosition()
    {
        var store = new Sekiban.Dcb.Testing.InMemoryEventStore(EventTypes);
        await store.WriteSerializableEventsAsync(new[] { Ev(P1), Ev(P2), Ev(P3) });

        var read = (await store.ReadAllSerializableEventsAsync(new SortableUniqueId(P2))).GetValue().ToList();

        Assert.Single(read);                                  // P1 (< P2) and P2 (== P2) excluded
        Assert.Equal(P3, read[0].SortableUniqueIdValue);      // only the strictly-later event
    }

    [Fact]
    public async Task SqliteStore_ReadAfterPosition_ExcludesTheEventAtThePosition()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"g18-exclusive-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteEventStore(dbPath, EventTypes);
            var write = await store.WriteSerializableEventsAsync(new[] { Ev(P1), Ev(P2), Ev(P3) });
            Assert.True(write.IsSuccess, write.IsSuccess ? "" : write.GetException().ToString());

            var read = (await store.ReadAllSerializableEventsAsync(new SortableUniqueId(P2))).GetValue().ToList();

            Assert.Single(read);
            Assert.Equal(P3, read[0].SortableUniqueIdValue);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                File.Delete(dbPath);
            }
        }
    }
}
