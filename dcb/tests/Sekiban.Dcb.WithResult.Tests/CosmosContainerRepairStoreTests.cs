using Microsoft.Azure.Cosmos;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.CosmosDb.Repair;
using System.Collections;
using System.Net;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     Drives the PRODUCTION repair store — <see cref="CosmosContainerRepairStore" />, the one that talks to
///     a real container — rather than an in-memory stand-in.
///     This is where the earlier bug lived: the store's SQL filtered on <c>c.eventId = eventId.ToString()</c>,
///     the canonical lowercase "D" rendering. A legacy row stored with a different Guid format or casing was
///     never returned by the server, so it looked exactly like a *missing* row and the repair would have
///     written a second row for a pair that was already indexed. The service-level tests passed anyway,
///     because their fake store parsed Guids client-side — they were testing the fake, not the query.
///     So these tests hold the real store to the rule: the SQL is only a superset prefilter, and the row's
///     event id is decided by canonical Guid comparison. The fake container below deliberately IGNORES the
///     SQL and hands back everything in the partition, which is the strongest form of the claim — even a
///     server that over-returns cannot make the store wrong, and a store that leaned on the predicate for
///     correctness would fail here.
/// </summary>
public class CosmosContainerRepairStoreTests
{
    private const string ServiceId = "svc";
    private const string Tag = "Student:1";
    private static string PartitionKey => $"{ServiceId}|{Tag}";

    private sealed class FakeFeedResponse : FeedResponse<CosmosTag>
    {
        private readonly IReadOnlyList<CosmosTag> _rows;

        public FakeFeedResponse(IReadOnlyList<CosmosTag> rows) => _rows = rows;

        public override string? ContinuationToken => null;
        public override int Count => _rows.Count;
        public override Headers Headers { get; } = new();
        public override IEnumerable<CosmosTag> Resource => _rows;
        public override double RequestCharge => 1.0;
        public override HttpStatusCode StatusCode => HttpStatusCode.OK;
        public override CosmosDiagnostics Diagnostics => null!;
        public override string IndexMetrics => string.Empty;

        public override IEnumerator<CosmosTag> GetEnumerator() => _rows.GetEnumerator();
    }

    private sealed class FakeFeedIterator : FeedIterator<CosmosTag>
    {
        private readonly IReadOnlyList<CosmosTag> _rows;
        private bool _served;

        public FakeFeedIterator(IReadOnlyList<CosmosTag> rows) => _rows = rows;

        public override bool HasMoreResults => !_served;

        public override Task<FeedResponse<CosmosTag>> ReadNextAsync(CancellationToken cancellationToken = default)
        {
            _served = true;
            return Task.FromResult<FeedResponse<CosmosTag>>(new FakeFeedResponse(_rows));
        }
    }

    /// <summary>
    ///     A container that ignores the SQL entirely and returns every row in the requested partition.
    ///     If the store relied on its predicate to exclude other events' rows, that would show up here.
    /// </summary>
    private sealed class FakePartitionContainer : NotSupportedCosmosContainer
    {
        private readonly List<CosmosTag> _rows;

        public FakePartitionContainer(IEnumerable<CosmosTag> rows) => _rows = rows.ToList();

        public QueryDefinition? LastQuery { get; private set; }

        public override FeedIterator<T> GetItemQueryIterator<T>(
            QueryDefinition queryDefinition,
            string? continuationToken = null,
            QueryRequestOptions? requestOptions = null)
        {
            LastQuery = queryDefinition;

            var partitionKey = (string)queryDefinition
                .GetQueryParameters()
                .First(parameter => parameter.Name == "@pk")
                .Value;

            var rows = _rows
                .Where(row => string.Equals(row.Pk, partitionKey, StringComparison.Ordinal))
                .ToList();

            return (FeedIterator<T>)(object)new FakeFeedIterator(rows);
        }
    }

    private static string NewSortableUniqueId() => SortableUniqueId.GenerateNew();

    private static CosmosTag LegacyRow(Guid eventId, string sortableUniqueId, string eventIdRendering) =>
        new()
        {
            Pk = PartitionKey,
            ServiceId = ServiceId,
            Id = Guid.NewGuid().ToString(), // legacy: a random row id, not the event id
            Tag = Tag,
            TagGroup = "Student",
            EventType = "TestEvent",
            SortableUniqueId = sortableUniqueId,
            EventId = eventIdRendering,
            CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

    [Theory]
    [InlineData("D")] // canonical, but upper-cased below
    [InlineData("N")] // no dashes
    [InlineData("B")] // braces
    [InlineData("P")] // parentheses
    [InlineData("X")] // hex object
    public async Task Should_Return_A_Legacy_Row_Whatever_Guid_Format_It_Was_Stored_In(string format)
    {
        var eventId = Guid.NewGuid();
        var sortableUniqueId = NewSortableUniqueId();

        // Upper-cased on purpose: casing must not decide the outcome either.
        var rendering = eventId.ToString(format).ToUpperInvariant();
        var container = new FakePartitionContainer(new[] { LegacyRow(eventId, sortableUniqueId, rendering) });
        var store = new CosmosContainerRepairStore(container);

        var lookup = await store.ReadRowsForEventAsync(PartitionKey, eventId, 16, CancellationToken.None);

        var row = Assert.Single(lookup.Rows);
        Assert.Equal(rendering, row.EventId);
        Assert.False(lookup.Overflowed);
    }

    [Fact]
    public async Task Should_Reject_Rows_Of_A_Different_Event_Even_When_The_Server_Over_Returns()
    {
        var ours = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var sortableUniqueId = NewSortableUniqueId();

        // Same tag partition, different event. The fake container hands back both rows.
        var container = new FakePartitionContainer(new[]
        {
            LegacyRow(ours, sortableUniqueId, ours.ToString()),
            LegacyRow(theirs, sortableUniqueId, theirs.ToString().ToUpperInvariant())
        });
        var store = new CosmosContainerRepairStore(container);

        var lookup = await store.ReadRowsForEventAsync(PartitionKey, ours, 16, CancellationToken.None);

        // The client-side canonical comparison is what excludes the other event — not the SQL.
        var row = Assert.Single(lookup.Rows);
        Assert.Equal(ours, Guid.Parse(row.EventId));
    }

    [Fact]
    public async Task Should_Ignore_A_Row_Whose_Event_Id_Is_Not_A_Guid()
    {
        var eventId = Guid.NewGuid();
        var sortableUniqueId = NewSortableUniqueId();

        var container = new FakePartitionContainer(new[]
        {
            LegacyRow(eventId, sortableUniqueId, "not-a-guid"),
            LegacyRow(eventId, sortableUniqueId, eventId.ToString("N"))
        });
        var store = new CosmosContainerRepairStore(container);

        var lookup = await store.ReadRowsForEventAsync(PartitionKey, eventId, 16, CancellationToken.None);

        var row = Assert.Single(lookup.Rows);
        Assert.Equal(eventId.ToString("N"), row.EventId);
    }

    [Fact]
    public async Task Should_Report_Overflow_On_Matching_Rows_Only()
    {
        var ours = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var sortableUniqueId = NewSortableUniqueId();

        // Three rows for our event (in mixed formats), plus one for another event.
        var container = new FakePartitionContainer(new[]
        {
            LegacyRow(ours, sortableUniqueId, ours.ToString("D")),
            LegacyRow(ours, sortableUniqueId, ours.ToString("N").ToUpperInvariant()),
            LegacyRow(ours, sortableUniqueId, ours.ToString("B")),
            LegacyRow(theirs, sortableUniqueId, theirs.ToString())
        });
        var store = new CosmosContainerRepairStore(container);

        var lookup = await store.ReadRowsForEventAsync(PartitionKey, ours, 2, CancellationToken.None);

        // The cap counts rows that index OUR event; the other event's row must not consume it.
        Assert.True(lookup.Overflowed);
        Assert.Equal(2, lookup.Rows.Count);
        Assert.All(lookup.Rows, row => Assert.Equal(ours, Guid.Parse(row.EventId)));
    }

    [Theory]
    [InlineData("D")]
    [InlineData("N")]
    [InlineData("B")]
    public async Task The_Service_On_The_Production_Store_Should_Classify_An_Odd_Format_Legacy_Row_As_LegacyPresent(
        string format)
    {
        var eventId = Guid.NewGuid();
        var sortableUniqueId = NewSortableUniqueId();
        var rendering = eventId.ToString(format).ToUpperInvariant();

        var container = new FakePartitionContainer(new[] { LegacyRow(eventId, sortableUniqueId, rendering) });
        var service = new CosmosDbTagRepairService(
            ServiceId,
            new SingleEventSource(eventId, sortableUniqueId),
            new CosmosContainerRepairStore(container));

        var report = await service.RepairAsync(new CosmosTagRepairOptions { DryRun = false });

        // The whole point: this row must not look "missing" just because of how its event id was rendered.
        Assert.Equal(1, report.LegacyPresent);
        Assert.Equal(0, report.Missing);
        Assert.Equal(0, report.Repaired);
        Assert.Equal(0, report.Corrupt);

        // Zero create attempts — proven by the fake container, whose CreateItemAsync throws.
    }

    [Theory]
    [InlineData("D")]
    [InlineData("N")]
    [InlineData("B")]
    public async Task The_Service_On_The_Production_Store_Should_Classify_Odd_Format_Legacy_Duplicates_As_Duplicate(
        string format)
    {
        var eventId = Guid.NewGuid();
        var sortableUniqueId = NewSortableUniqueId();

        // Two legacy rows for the same pair, one of them in an odd rendering.
        var container = new FakePartitionContainer(new[]
        {
            LegacyRow(eventId, sortableUniqueId, eventId.ToString()),
            LegacyRow(eventId, sortableUniqueId, eventId.ToString(format).ToUpperInvariant())
        });
        var service = new CosmosDbTagRepairService(
            ServiceId,
            new SingleEventSource(eventId, sortableUniqueId),
            new CosmosContainerRepairStore(container));

        var report = await service.RepairAsync(new CosmosTagRepairOptions { DryRun = false });

        Assert.Equal(1, report.Duplicate);
        Assert.Equal(0, report.Missing);
        Assert.Equal(0, report.Repaired);
    }

    /// <summary>Serves exactly one event, so these tests exercise the real store, not a fake one.</summary>
    private sealed class SingleEventSource : ICosmosRepairEventSource
    {
        private readonly CosmosEvent _event;

        public SingleEventSource(Guid eventId, string sortableUniqueId) =>
            _event = new CosmosEvent
            {
                Pk = $"{ServiceId}|{eventId}",
                ServiceId = ServiceId,
                Id = eventId.ToString(),
                SortableUniqueId = sortableUniqueId,
                EventType = "TestEvent",
                Payload = "{}",
                Tags = new List<string> { Tag }
            };

        public Task<CosmosRepairEventPage> ReadEventPageAsync(
            string? fromSortableUniqueIdExclusive,
            string? toSortableUniqueIdInclusive,
            int pageSize,
            string? continuationToken,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CosmosRepairEventPage(
                continuationToken == null ? new[] { _event } : Array.Empty<CosmosEvent>(),
                null,
                1.0));
    }

    [Fact]
    public async Task The_Sql_Prefilter_Should_Enumerate_Every_Guid_Rendering_Case_Insensitively()
    {
        var eventId = Guid.NewGuid();
        var container = new FakePartitionContainer(Array.Empty<CosmosTag>());
        var store = new CosmosContainerRepairStore(container);

        await store.ReadRowsForEventAsync(PartitionKey, eventId, 16, CancellationToken.None);

        var query = container.LastQuery!;
        var text = query.QueryText;
        var parameters = query.GetQueryParameters().Select(p => (string)p.Value).ToList();

        // A server-side equality on one rendering would silently drop the others, so the prefilter must not
        // be one: it enumerates every format Guid.ToString can emit, compared case-insensitively.
        Assert.Contains("STRINGEQUALS", text, StringComparison.Ordinal);
        Assert.DoesNotContain("c.eventId =", text, StringComparison.Ordinal);
        foreach (var format in new[] { "D", "N", "B", "P", "X" })
        {
            Assert.Contains(eventId.ToString(format), parameters);
        }
    }
}
