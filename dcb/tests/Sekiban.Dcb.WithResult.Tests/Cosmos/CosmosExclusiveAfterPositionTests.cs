using Dcb.Domain;
using Dcb.Domain.Student;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Xunit;
namespace Sekiban.Dcb.Tests.Cosmos;

/// <summary>
///     SEK-G18 (#1086) provider boundary matrix — Cosmos. Drives the REAL <see cref="CosmosDbEventStore" /> read
///     construction (`SELECT * FROM c WHERE c.serviceId = @serviceId AND c.sortableUniqueId &gt; @since ...`) through the
///     in-memory Cosmos container, which faithfully honors the operator emitted by the production query text. Writing
///     P1&lt;P2&lt;P3 and reading since=P2 must yield ONLY P3 — P1 (&lt; P2) and the at-position P2 (== P2) both excluded.
///     If production regressed `&gt; @since` to `&gt;= @since`, the container would return P2 and this fails (non-vacuous).
/// </summary>
public class CosmosExclusiveAfterPositionTests
{
    private static readonly DateTime BaseTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string P1 = SortableUniqueId.Generate(BaseTime, Guid.Empty);
    private static readonly string P2 = SortableUniqueId.Generate(BaseTime.AddSeconds(1), Guid.Empty);
    private static readonly string P3 = SortableUniqueId.Generate(BaseTime.AddSeconds(2), Guid.Empty);

    private sealed class FixedServiceIdProvider : IServiceIdProvider
    {
        private readonly string _serviceId;
        public FixedServiceIdProvider(string serviceId) => _serviceId = serviceId;
        public string GetCurrentServiceId() => _serviceId;
    }

    private static CosmosDbEventStore NewStore(string serviceId)
    {
        var options = new CosmosDbEventStoreOptions { EventsContainerName = "events", TagsContainerName = "tags" };
        var context = new CosmosDbContext(new InMemoryCosmosClient(), "test-db", null, options);
        var resolver = new DefaultCosmosContainerResolver(options);
        return new CosmosDbEventStore(
            context, DomainType.GetDomainTypes().EventTypes, new FixedServiceIdProvider(serviceId), resolver);
    }

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
        var store = NewStore("svc");
        Assert.True((await store.WriteEventsAsync(new[] { Ev(P1), Ev(P2), Ev(P3) })).IsSuccess);

        var read = (await store.ReadAllEventsAsync(new SortableUniqueId(P2))).GetValue().ToList();

        Assert.Single(read);                                  // P1 (< P2) and P2 (== P2) excluded
        Assert.Equal(P3, read[0].SortableUniqueIdValue);      // only the strictly-later event
    }
}
