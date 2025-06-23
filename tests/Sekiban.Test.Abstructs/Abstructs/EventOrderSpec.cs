using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
using FeatureCheck.Domain.Shared;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Testing.Story;
using Xunit.Abstractions;
namespace Sekiban.Test.Abstructs.Abstructs;

public abstract class EventOrderSpec(
    TestBase<FeatureCheckDependency>.SekibanTestFixture sekibanTestFixture,
    ITestOutputHelper output,
    ISekibanServiceProviderGenerator providerGenerator)
    : TestBase<FeatureCheckDependency>(sekibanTestFixture, output, providerGenerator)
{
    [Fact]
    public async Task EventOrderTest()
    {
        RemoveAllFromDefaultAndDissolvable();

        var beforeSortableUniqueId = SortableUniqueIdValue.GetCurrentIdFromUtc();

        var branchResponse = await commandExecutor.ExecCommandAsync(new CreateBranch("Test"));
        var branchId = branchResponse.AggregateId!.Value;
        var clientResponse
            = await commandExecutor.ExecCommandAsync(new CreateClient(branchId, "Test Name", "test@example.com"));
        var clientId = clientResponse.AggregateId!.Value;

        var refVersion = 0;
        refVersion = clientResponse.Version;
        foreach (var i in Enumerable.Range(1, 100))
        {
            sekibanTestFixture.TestOutputHelper?.WriteLine($"i:{i}");
            var response = await commandExecutor.ExecCommandAsync(
                new AddLoyaltyPoint(
                    clientId,
                    DateTime.UtcNow,
                    LoyaltyPointReceiveTypeKeys.CreditcardUsage,
                    i * 100,
                    $"test{i}") { ReferenceVersion = refVersion });
            refVersion = response.Version;
        }
        // check if aggregate order is correct
        var repository = serviceProvider.GetRequiredService<EventRepository>();
        var eventsOldToNew = new List<IEvent>();
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(
                IDocument.DefaultRootPartitionKey,
                new AggregateTypeStream<LoyaltyPoint>(),
                clientId,
                ISortableIdCondition.None),
            m => eventsOldToNew.AddRange(m));
        Assert.Equal(101, eventsOldToNew.Count);



        // check if all event order and max count is correct
        eventsOldToNew = [];
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(
                IDocument.DefaultRootPartitionKey,
                new AggregateTypeStream<LoyaltyPoint>(),
                clientId,
                ISortableIdCondition.None,
                20),
            m => eventsOldToNew.AddRange(m));
        Assert.Equal(20, eventsOldToNew.Count);





        // check if all event order is correct
        eventsOldToNew = [];
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(null, new AllStream(), null, ISortableIdCondition.None),
            m => eventsOldToNew.AddRange(m));
        Assert.Equal(103, eventsOldToNew.Count);

        // check if all event order and max count is correct
        eventsOldToNew = [];
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(null, new AllStream(), null, ISortableIdCondition.None, 20),
            m => eventsOldToNew.AddRange(m));
        Assert.Equal(20, eventsOldToNew.Count);



        var secondSortableUniqueId = SortableUniqueIdValue.GetCurrentIdFromUtc();


        foreach (var i in Enumerable.Range(1, 50))
        {
            sekibanTestFixture.TestOutputHelper?.WriteLine($"i:{i}");
            var response = await commandExecutor.ExecCommandAsync(
                new AddLoyaltyPoint(
                    clientId,
                    DateTime.UtcNow,
                    LoyaltyPointReceiveTypeKeys.CreditcardUsage,
                    i * 100,
                    $"test{i}") { ReferenceVersion = refVersion });
            refVersion = response.Version;
        }

        // single partition between test
        eventsOldToNew = [];
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(null, new AggregateTypeStream<LoyaltyPoint>(), clientId, ISortableIdCondition.Between(beforeSortableUniqueId, secondSortableUniqueId), null),
            m => eventsOldToNew.AddRange(m));
        Assert.Equal(101, eventsOldToNew.Count);

        // single partition boundary test
        var sortableUniqueIdLowerBound = eventsOldToNew[0].SortableUniqueId;
        var sortableUniqueIdUpperBound = eventsOldToNew[^1].SortableUniqueId;
        eventsOldToNew = [];
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(null, new AggregateTypeStream<LoyaltyPoint>(), clientId, ISortableIdCondition.Between(sortableUniqueIdLowerBound, sortableUniqueIdUpperBound), null),
            m => eventsOldToNew.AddRange(m));
        Assert.Equal(99, eventsOldToNew.Count);

        // single partition between test with max count
        eventsOldToNew = [];
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(null, new AggregateTypeStream<LoyaltyPoint>(), clientId, ISortableIdCondition.Between(beforeSortableUniqueId, secondSortableUniqueId), 30),
            m => eventsOldToNew.AddRange(m));
        Assert.Equal(30, eventsOldToNew.Count);

        // all partition between test
        eventsOldToNew = [];
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(null, new AllStream(), null, ISortableIdCondition.Between(beforeSortableUniqueId, secondSortableUniqueId), null),
            m => eventsOldToNew.AddRange(m));
        Assert.Equal(103, eventsOldToNew.Count);

        // all partitions boundary test
        sortableUniqueIdLowerBound = eventsOldToNew[0].SortableUniqueId;
        sortableUniqueIdUpperBound = eventsOldToNew[^1].SortableUniqueId;
        eventsOldToNew = [];
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(null, new AllStream(), null, ISortableIdCondition.Between(sortableUniqueIdLowerBound, sortableUniqueIdUpperBound), null),
            m => eventsOldToNew.AddRange(m));
        Assert.Equal(101, eventsOldToNew.Count);

        var thirdSortableUniqueId = SortableUniqueIdValue.GetCurrentIdFromUtc();

        // all partition between test
        eventsOldToNew = [];
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(null, new AllStream(), null, ISortableIdCondition.Between(secondSortableUniqueId, thirdSortableUniqueId), null),
            m => eventsOldToNew.AddRange(m));
        Assert.Equal(50, eventsOldToNew.Count);

        // all partition between test with max count
        eventsOldToNew = [];
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(null, new AllStream(), null, ISortableIdCondition.Between(secondSortableUniqueId, thirdSortableUniqueId), 30),
            m => eventsOldToNew.AddRange(m));
        Assert.Equal(30, eventsOldToNew.Count);

    }
}
