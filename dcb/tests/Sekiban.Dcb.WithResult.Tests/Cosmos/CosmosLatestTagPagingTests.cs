using Dcb.Domain;
using Dcb.Domain.Student;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.Tests.Cosmos;

public class CosmosLatestTagPagingTests
{
    private sealed class Service : IServiceIdProvider
    {
        public string GetCurrentServiceId() => "svc";
    }

    private sealed record TestTag : ITag
    {
        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => "Student";
        public string GetTagContent() => "paging";
    }

    [Fact]
    public async Task LatestTagFollowsEmptyFirstPageToTheRow()
    {
        var client = new InMemoryCosmosClient();
        var options = new CosmosDbEventStoreOptions();
        using var context = new CosmosDbContext(client, "test-db", null, options);
        var store = new CosmosDbEventStore(context, DomainType.GetDomainTypes().EventTypes,
            new Service(), new DefaultCosmosContainerResolver(options));
        var tag = new TestTag();
        var position = SortableUniqueId.GenerateNew();
        var ev = new Event(new StudentCreated(Guid.NewGuid(), "n", 5), position,
            nameof(StudentCreated), Guid.NewGuid(), new EventMetadata("c", "c", "test"), [((ITag)tag).GetTag()]);
        Assert.True((await store.WriteEventsAsync([ev])).IsSuccess);
        var container = client.Container(options.TagsContainerName);
        container.EmptyFirstQueryPage = true;
        var readsBefore = container.QueryReadTokens.Count;

        var result = await store.GetLatestTagAsync(tag);

        Assert.True(result.IsSuccess);
        Assert.Equal(position, result.GetValue().LastSortedUniqueId);
        Assert.Equal(2, container.QueryReadTokens.Count - readsBefore);
    }
}
