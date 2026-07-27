using Dcb.Domain.Student;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Xunit;
namespace Sekiban.Dcb.Postgres.Tests;

/// <summary>
///     SEK-G18 (#1086) provider boundary matrix — Postgres. The catch-up read after a restore starts from
///     <c>record.LastSortableUniqueId</c> and MUST be EXCLUSIVE of that position: an event whose SortableUniqueId equals
///     the checkpoint position is already reflected in the restored payload and must NOT be re-read (which would
///     double-count on re-fold). This pins the exclusive-after-position boundary against the REAL Postgres store
///     (Testcontainers) with literal at-position vectors, matching the in-memory + SQLite pins in the core test project.
///     (Postgres/Cosmos/DynamoDB all filter with the same <c>SortableUniqueId &gt; @since</c> predicate; Postgres is the
///     runnable provider proof in CI.)
/// </summary>
public class PostgresExclusiveAfterPositionTests : PostgresTestBase
{
    public PostgresExclusiveAfterPositionTests(PostgresTestFixture fixture) : base(fixture)
    {
    }

    // Three ordinally-sortable positions with valid tick prefixes. P2 is the "at position" boundary vector: a read
    // since=P2 must exclude P1 (< P2) and P2 (== P2), yielding only P3.
    private static readonly DateTime BaseTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string P1 = SortableUniqueId.Generate(BaseTime, Guid.Empty);
    private static readonly string P2 = SortableUniqueId.Generate(BaseTime.AddSeconds(1), Guid.Empty);
    private static readonly string P3 = SortableUniqueId.Generate(BaseTime.AddSeconds(2), Guid.Empty);

    private static Event Ev(string sortableId) => new(
        new StudentCreated(Guid.NewGuid(), "n", 5),
        sortableId,
        nameof(StudentCreated),
        Guid.NewGuid(),
        new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "test"),
        new List<string>());

    [Fact]
    public async Task ReadAllEventsAsync_AfterPosition_ExcludesTheEventAtThePosition()
    {
        await Fixture.EventStore.WriteEventsAsync(new[] { Ev(P1), Ev(P2), Ev(P3) });

        var read = (await Fixture.EventStore.ReadAllEventsAsync(new SortableUniqueId(P2))).GetValue().ToList();

        Assert.Single(read);                                  // P1 (< P2) and P2 (== P2) excluded
        Assert.Equal(P3, read[0].SortableUniqueIdValue);      // only the strictly-later event
    }

    [Fact]
    public async Task ReadAllSerializableEventsAsync_AfterPosition_ExcludesTheEventAtThePosition()
    {
        await Fixture.EventStore.WriteEventsAsync(new[] { Ev(P1), Ev(P2), Ev(P3) });

        var read = (await Fixture.EventStore.ReadAllSerializableEventsAsync(new SortableUniqueId(P2))).GetValue().ToList();

        Assert.Single(read);
        Assert.Equal(P3, read[0].SortableUniqueIdValue);
    }
}
