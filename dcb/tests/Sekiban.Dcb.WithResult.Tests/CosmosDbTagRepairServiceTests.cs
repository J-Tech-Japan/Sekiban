using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.CosmosDb.Repair;
using Sekiban.Dcb.CosmosDb.Tags;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Covers the tag-repair service: repairing genuinely-missing rows, recognizing pre-SEK-G2 legacy rows
///     without touching them, refusing to overwrite anything that disagrees with the event, dry-run,
///     checkpoint/resume, idempotency, and a concurrent writer racing the repair.
/// </summary>
public class CosmosDbTagRepairServiceTests
{
    private const string ServiceId = "svc";
    private const string OtherServiceId = "other-svc";

    /// <summary>In-memory events container, ordered by sortableUniqueId, paged like the real scan.</summary>
    private sealed class FakeEventSource : ICosmosRepairEventSource
    {
        private readonly List<CosmosEvent> _events;

        public FakeEventSource(IEnumerable<CosmosEvent> events) =>
            _events = events.OrderBy(e => e.SortableUniqueId, StringComparer.Ordinal).ToList();

        public int PagesServed { get; private set; }

        public Task<CosmosRepairEventPage> ReadEventPageAsync(
            string? fromSortableUniqueIdExclusive,
            string? toSortableUniqueIdInclusive,
            int pageSize,
            string? continuationToken,
            CancellationToken cancellationToken)
        {
            PagesServed++;

            var offset = continuationToken == null ? 0 : int.Parse(continuationToken, null);
            var candidates = _events
                .Where(e => fromSortableUniqueIdExclusive == null ||
                    string.CompareOrdinal(e.SortableUniqueId, fromSortableUniqueIdExclusive) > 0)
                .Where(e => toSortableUniqueIdInclusive == null ||
                    string.CompareOrdinal(e.SortableUniqueId, toSortableUniqueIdInclusive) <= 0)
                .ToList();

            var page = candidates.Skip(offset).Take(pageSize).ToList();
            var consumed = offset + page.Count;
            var next = consumed < candidates.Count ? consumed.ToString(null as IFormatProvider) : null;

            return Task.FromResult(new CosmosRepairEventPage(page, next, 1.0));
        }
    }

    /// <summary>In-memory tags container. Records every write so "non-destructive" can be asserted, not assumed.</summary>
    private sealed class FakeRepairStore : ICosmosTagRepairStore
    {
        private readonly Dictionary<(string PartitionKey, string Id), CosmosTag> _rows = new();

        public int CreateAttempts { get; private set; }
        public Func<Task>? BeforeCreate { get; set; }
        public IReadOnlyCollection<CosmosTag> Rows => _rows.Values;

        public void Seed(CosmosTag row) => _rows[(row.Pk, row.Id)] = row;

        public Task<CosmosRepairRowLookup> ReadRowsForEventAsync(
            string partitionKey,
            Guid eventId,
            int maxRows,
            CancellationToken cancellationToken)
        {
            var matches = _rows.Values
                .Where(row => string.Equals(row.Pk, partitionKey, StringComparison.Ordinal))
                .Where(row => Guid.TryParse(row.EventId, out var id) && id == eventId)
                .ToList();

            return Task.FromResult(matches.Count > maxRows
                ? new CosmosRepairRowLookup(matches.Take(maxRows).ToList(), true, 1.0)
                : new CosmosRepairRowLookup(matches, false, 1.0));
        }

        public async Task<(bool Created, double RequestCharge)> TryCreateRowAsync(
            string partitionKey,
            CosmosTag row,
            CancellationToken cancellationToken)
        {
            CreateAttempts++;

            if (BeforeCreate != null)
            {
                await BeforeCreate().ConfigureAwait(false);
            }

            if (_rows.ContainsKey((partitionKey, row.Id)))
            {
                return (false, 1.0);
            }

            _rows[(partitionKey, row.Id)] = row;
            return (true, 1.0);
        }

        public Task<CosmosTag?> TryReadRowAsync(string partitionKey, string id, CancellationToken cancellationToken) =>
            Task.FromResult(_rows.GetValueOrDefault((partitionKey, id)));
    }

    private static string NewSortableUniqueId() => SortableUniqueId.GenerateNew();

    private static CosmosEvent Event(Guid id, string sortableUniqueId, params string[] tags) =>
        new()
        {
            Pk = $"{ServiceId}|{id}",
            ServiceId = ServiceId,
            Id = id.ToString(),
            SortableUniqueId = sortableUniqueId,
            EventType = "TestEvent",
            Payload = "{}",
            Tags = tags.ToList()
        };

    /// <summary>A row as the pre-SEK-G2 writer produced it: random id, wall-clock createdAt.</summary>
    private static CosmosTag LegacyRow(Guid eventId, string tag, string sortableUniqueId, string? id = null) =>
        new()
        {
            Pk = $"{ServiceId}|{tag}",
            ServiceId = ServiceId,
            Id = id ?? Guid.NewGuid().ToString(),
            Tag = tag,
            TagGroup = tag.Contains(':', StringComparison.Ordinal) ? tag.Split(':')[0] : tag,
            EventType = "TestEvent",
            SortableUniqueId = sortableUniqueId,
            EventId = eventId.ToString(),
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

    private static CosmosDbTagRepairService Service(FakeEventSource source, FakeRepairStore store) =>
        new(ServiceId, source, store);

    private static CosmosTagRepairOptions Repair() => new() { DryRun = false };

    [Fact]
    public async Task Should_Repair_A_Missing_Tag_Row()
    {
        var eventId = Guid.NewGuid();
        var sortableUniqueId = NewSortableUniqueId();
        var store = new FakeRepairStore();
        var service = Service(new FakeEventSource(new[] { Event(eventId, sortableUniqueId, "Student:1") }), store);

        var report = await service.RepairAsync(Repair());

        Assert.Equal(1, report.EventsScanned);
        Assert.Equal(1, report.KeysScanned);
        Assert.Equal(1, report.Missing);
        Assert.Equal(1, report.Repaired);
        Assert.False(report.DryRun);

        var row = Assert.Single(store.Rows);
        Assert.Equal(eventId.ToString(), row.Id);
        Assert.Equal($"{ServiceId}|Student:1", row.Pk);
        Assert.True(CosmosTagIdentity.ContentEquals(
            CosmosTagIdentity.DeriveRow(ServiceId, "Student:1", eventId, sortableUniqueId, "TestEvent"),
            row));
    }

    [Fact]
    public async Task Dry_Run_Should_Report_Without_Writing()
    {
        var store = new FakeRepairStore();
        var service = Service(
            new FakeEventSource(new[] { Event(Guid.NewGuid(), NewSortableUniqueId(), "Student:1") }),
            store);

        var report = await service.RepairAsync(new CosmosTagRepairOptions { DryRun = true });

        Assert.True(report.DryRun);
        Assert.Equal(1, report.Missing);
        Assert.Equal(0, report.Repaired);
        Assert.Empty(store.Rows);
        Assert.Equal(0, store.CreateAttempts);
    }

    [Fact]
    public async Task Re_Running_A_Repair_Should_Be_Idempotent()
    {
        var store = new FakeRepairStore();
        var source = new FakeEventSource(new[] { Event(Guid.NewGuid(), NewSortableUniqueId(), "Student:1") });

        var first = await Service(source, store).RepairAsync(Repair());
        var second = await Service(source, store).RepairAsync(Repair());

        Assert.Equal(1, first.Repaired);
        Assert.Equal(0, second.Repaired);
        Assert.Equal(1, second.Present);
        Assert.Single(store.Rows);
    }

    [Fact]
    public async Task Two_Events_Sharing_A_Tag_Should_Not_Match_Each_Other()
    {
        // The legacy row belongs to eventA. eventB carries the same tag, so it lives in the same partition —
        // but it is a different event and must still be seen as missing.
        var eventA = Guid.NewGuid();
        var eventB = Guid.NewGuid();
        var sortableA = NewSortableUniqueId();
        var sortableB = NewSortableUniqueId();

        var store = new FakeRepairStore();
        store.Seed(LegacyRow(eventA, "Student:1", sortableA));

        var service = Service(
            new FakeEventSource(new[]
            {
                Event(eventA, sortableA, "Student:1"),
                Event(eventB, sortableB, "Student:1")
            }),
            store);

        var report = await service.RepairAsync(Repair());

        Assert.Equal(1, report.LegacyPresent);
        Assert.Equal(1, report.Missing);
        Assert.Equal(1, report.Repaired);
        Assert.Equal(0, report.Corrupt);

        // eventA keeps only its legacy row; eventB gets a fresh deterministic row.
        Assert.Equal(2, store.Rows.Count);
        Assert.Contains(store.Rows, row => row.EventId == eventA.ToString() && row.Id != eventA.ToString());
        Assert.Contains(store.Rows, row => row.Id == eventB.ToString());
    }

    [Theory]
    [InlineData("D")] // 00000000-0000-0000-0000-000000000000
    [InlineData("N")] // 00000000000000000000000000000000
    [InlineData("B")] // {00000000-...}
    public async Task Legacy_Rows_Should_Match_Regardless_Of_Guid_Formatting(string format)
    {
        var eventId = Guid.NewGuid();
        var sortableUniqueId = NewSortableUniqueId();

        var legacy = LegacyRow(eventId, "Student:1", sortableUniqueId);
        legacy.EventId = eventId.ToString(format).ToUpperInvariant();

        var store = new FakeRepairStore();
        store.Seed(legacy);

        var report = await Service(
                new FakeEventSource(new[] { Event(eventId, sortableUniqueId, "Student:1") }),
                store)
            .RepairAsync(Repair());

        Assert.Equal(1, report.LegacyPresent);
        Assert.Equal(0, report.Missing);
        Assert.Equal(0, report.Repaired);
        Assert.Single(store.Rows);
    }

    [Fact]
    public async Task A_Legacy_Row_Should_Be_Legacy_Present_Not_Corrupt_And_Left_Untouched()
    {
        var eventId = Guid.NewGuid();
        var sortableUniqueId = NewSortableUniqueId();
        var legacy = LegacyRow(eventId, "Student:1", sortableUniqueId);
        var legacyId = legacy.Id;
        var legacyCreatedAt = legacy.CreatedAt;

        var store = new FakeRepairStore();
        store.Seed(legacy);

        var report = await Service(
                new FakeEventSource(new[] { Event(eventId, sortableUniqueId, "Student:1") }),
                store)
            .RepairAsync(Repair());

        // The random id and wall-clock createdAt are exactly the differences a legacy row is expected to
        // have. They are migration metadata, not corruption.
        Assert.Equal(1, report.LegacyPresent);
        Assert.Equal(0, report.Corrupt);
        Assert.Equal(0, report.Repaired);
        Assert.Equal(0, store.CreateAttempts);

        var stored = Assert.Single(store.Rows);
        Assert.Equal(legacyId, stored.Id);
        Assert.Equal(legacyCreatedAt, stored.CreatedAt);
    }

    [Theory]
    [InlineData("sortableUniqueId")]
    [InlineData("eventType")]
    [InlineData("tagGroup")]
    public async Task A_Legacy_Row_Disagreeing_With_The_Event_Should_Be_Corrupt(string driftedField)
    {
        var eventId = Guid.NewGuid();
        var sortableUniqueId = NewSortableUniqueId();
        var legacy = LegacyRow(eventId, "Student:1", sortableUniqueId);

        switch (driftedField)
        {
            case "sortableUniqueId":
                legacy.SortableUniqueId = NewSortableUniqueId();
                break;
            case "eventType":
                legacy.EventType = "SomethingElse";
                break;
            default:
                legacy.TagGroup = "Teacher";
                break;
        }

        var store = new FakeRepairStore();
        store.Seed(legacy);

        var report = await Service(
                new FakeEventSource(new[] { Event(eventId, sortableUniqueId, "Student:1") }),
                store)
            .RepairAsync(Repair());

        // A legacy id excuses the id and the timestamp. It does not license drift in what the row indexes.
        Assert.Equal(1, report.Corrupt);
        Assert.Equal(0, report.LegacyPresent);
        Assert.Equal(0, report.Repaired);
        Assert.Equal(0, store.CreateAttempts);
        Assert.Single(store.Rows);
        Assert.Contains(report.Findings, f => f.Category == CosmosTagRepairCategory.Corrupt && f.Detail != null);
    }

    [Fact]
    public async Task A_Deterministic_Row_With_A_Drifted_CreatedAt_Should_Still_Be_Corrupt()
    {
        var eventId = Guid.NewGuid();
        var sortableUniqueId = NewSortableUniqueId();

        // Sits at the derived id, so it is held to the strict SEK-G2 comparator — createdAt included.
        var deterministic = CosmosTagIdentity.DeriveRow(ServiceId, "Student:1", eventId, sortableUniqueId, "TestEvent");
        deterministic.CreatedAt = deterministic.CreatedAt.AddDays(1);

        var store = new FakeRepairStore();
        store.Seed(deterministic);

        var report = await Service(
                new FakeEventSource(new[] { Event(eventId, sortableUniqueId, "Student:1") }),
                store)
            .RepairAsync(Repair());

        Assert.Equal(1, report.Corrupt);
        Assert.Equal(0, report.Present);
        Assert.Equal(0, report.Repaired);
        Assert.Equal(0, store.CreateAttempts);
    }

    [Fact]
    public async Task Multiple_Legacy_Rows_Should_Be_Reported_As_Duplicate_And_Not_Mutated()
    {
        var eventId = Guid.NewGuid();
        var sortableUniqueId = NewSortableUniqueId();

        var store = new FakeRepairStore();
        store.Seed(LegacyRow(eventId, "Student:1", sortableUniqueId));
        store.Seed(LegacyRow(eventId, "Student:1", sortableUniqueId));

        var report = await Service(
                new FakeEventSource(new[] { Event(eventId, sortableUniqueId, "Student:1") }),
                store)
            .RepairAsync(Repair());

        // Reducing duplicates is destructive. This service reports them and stops.
        Assert.Equal(1, report.Duplicate);
        Assert.Equal(0, report.Repaired);
        Assert.Equal(0, store.CreateAttempts);
        Assert.Equal(2, store.Rows.Count);
    }

    [Fact]
    public async Task Rows_Beyond_The_Per_Key_Cap_Should_Be_Reported_As_Overflow()
    {
        var eventId = Guid.NewGuid();
        var sortableUniqueId = NewSortableUniqueId();

        var store = new FakeRepairStore();
        for (var i = 0; i < 4; i++)
        {
            store.Seed(LegacyRow(eventId, "Student:1", sortableUniqueId));
        }

        var report = await Service(
                new FakeEventSource(new[] { Event(eventId, sortableUniqueId, "Student:1") }),
                store)
            .RepairAsync(new CosmosTagRepairOptions { DryRun = false, MaxRowsPerKey = 2 });

        Assert.Equal(1, report.Overflow);
        Assert.Equal(0, report.Repaired);
        Assert.Equal(0, store.CreateAttempts);
        Assert.Equal(4, store.Rows.Count);
    }

    [Fact]
    public async Task A_Concurrent_Writer_Between_Classification_And_Repair_Should_Not_Duplicate_Or_Error()
    {
        var eventId = Guid.NewGuid();
        var sortableUniqueId = NewSortableUniqueId();
        var store = new FakeRepairStore();

        // The write path lands the very row we classified as missing, just before we create it.
        store.BeforeCreate = () =>
        {
            store.BeforeCreate = null;
            store.Seed(CosmosTagIdentity.DeriveRow(ServiceId, "Student:1", eventId, sortableUniqueId, "TestEvent"));
            return Task.CompletedTask;
        };

        var report = await Service(
                new FakeEventSource(new[] { Event(eventId, sortableUniqueId, "Student:1") }),
                store)
            .RepairAsync(Repair());

        // The row that landed is the row we were about to write, so this is a no-op, not a conflict.
        Assert.Equal(0, report.Corrupt);
        Assert.Equal(0, report.Repaired);
        Assert.Equal(1, report.Present);
        Assert.Single(store.Rows);
    }

    [Fact]
    public async Task A_Scan_Should_Stop_At_Its_Event_Budget_And_Resume_From_The_Checkpoint()
    {
        var events = Enumerable
            .Range(0, 5)
            .Select(_ => Event(Guid.NewGuid(), NewSortableUniqueId(), "Student:1"))
            .ToList();

        var store = new FakeRepairStore();
        var source = new FakeEventSource(events);

        var first = await Service(source, store).RepairAsync(
            new CosmosTagRepairOptions { DryRun = false, MaxEventsToScan = 2, PageSize = 2 });

        Assert.Equal(2, first.EventsScanned);
        Assert.Equal(2, first.Repaired);
        Assert.True(first.HasMore);
        Assert.NotNull(first.Checkpoint);

        var second = await Service(source, store).RepairAsync(
            new CosmosTagRepairOptions
            {
                DryRun = false,
                MaxEventsToScan = 100,
                PageSize = 2,
                Checkpoint = first.Checkpoint
            });

        // Resuming picks up exactly the events the first run did not reach — no gap, no re-repair.
        Assert.Equal(3, second.EventsScanned);
        Assert.Equal(3, second.Repaired);
        Assert.False(second.HasMore);
        Assert.Null(second.Checkpoint);
        Assert.Equal(5, store.Rows.Count);
    }

    [Fact]
    public async Task A_Scan_Should_Honor_Its_SortableUniqueId_Range()
    {
        var events = Enumerable
            .Range(0, 4)
            .Select(_ => Event(Guid.NewGuid(), NewSortableUniqueId(), "Student:1"))
            .OrderBy(e => e.SortableUniqueId, StringComparer.Ordinal)
            .ToList();

        var store = new FakeRepairStore();

        var report = await Service(new FakeEventSource(events), store).RepairAsync(
            new CosmosTagRepairOptions
            {
                DryRun = false,
                FromSortableUniqueIdExclusive = events[0].SortableUniqueId,
                ToSortableUniqueIdInclusive = events[2].SortableUniqueId
            });

        Assert.Equal(2, report.EventsScanned);
        Assert.Equal(2, report.Repaired);
        Assert.Equal(2, store.Rows.Count);
    }

    [Fact]
    public async Task Cancellation_Should_Stop_The_Scan()
    {
        using var cts = new CancellationTokenSource();
        var store = new FakeRepairStore();
        store.BeforeCreate = () =>
        {
            cts.Cancel();
            return Task.CompletedTask;
        };

        var events = Enumerable
            .Range(0, 5)
            .Select(_ => Event(Guid.NewGuid(), NewSortableUniqueId(), "Student:1"))
            .ToList();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service(new FakeEventSource(events), store).RepairAsync(
                new CosmosTagRepairOptions { DryRun = false, PageSize = 1 },
                cts.Token));
    }

    [Fact]
    public void A_Repair_Service_Is_Bound_To_One_Service_Id()
    {
        var service = Service(new FakeEventSource(Array.Empty<CosmosEvent>()), new FakeRepairStore());

        // The lineage is fixed at construction, so a run cannot be pointed at another tenant's containers.
        Assert.Equal(ServiceId, service.ServiceId);
        Assert.NotEqual(OtherServiceId, service.ServiceId);
    }

    [Fact]
    public async Task A_Tag_Repeated_Within_One_Event_Should_Be_One_Key()
    {
        var eventId = Guid.NewGuid();
        var store = new FakeRepairStore();
        var cosmosEvent = Event(eventId, NewSortableUniqueId(), "Student:1", "Student:1");

        var report = await Service(new FakeEventSource(new[] { cosmosEvent }), store).RepairAsync(Repair());

        Assert.Equal(1, report.KeysScanned);
        Assert.Equal(1, report.Repaired);
        Assert.Single(store.Rows);
    }
}
