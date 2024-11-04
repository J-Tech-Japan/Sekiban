using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
using FeatureCheck.Domain.Shared;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
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
        var branchResponse = await commandExecutor.ExecCommandAsync(new CreateBranch("Test"));
        var branchId = branchResponse.AggregateId!.Value;
        var clientResponse
            = await commandExecutor.ExecCommandAsync(new CreateClient(branchId, "Test Name", "test@example.com"));
        var clientId = clientResponse.AggregateId!.Value;

        var refVersion = 0;
        refVersion = clientResponse.Version;
        foreach (var i in Enumerable.Range(1, 100))
        {
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
                null),
            m => eventsOldToNew.AddRange(m));
        var eventsNewToOld = new List<IEvent>();
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(
                IDocument.DefaultRootPartitionKey,
                new AggregateTypeStream<LoyaltyPoint>(),
                clientId,
                null,
                RetrieveEventOrder.NewToOld),
            m => eventsNewToOld.AddRange(m));
        Assert.Equal(101, eventsOldToNew.Count);
        Assert.Equal(101, eventsNewToOld.Count);
        Assert.NotEqual(eventsOldToNew[0], eventsNewToOld[0]);
        Assert.Equal(eventsOldToNew.First().Id, eventsNewToOld.Last().Id);



        // check if all event order and max count is correct
        eventsOldToNew = new List<IEvent>();
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(
                IDocument.DefaultRootPartitionKey,
                new AggregateTypeStream<LoyaltyPoint>(),
                clientId,
                null,
                RetrieveEventOrder.OldToNew,
                20),
            m => eventsOldToNew.AddRange(m));
        eventsNewToOld = new List<IEvent>();
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(
                IDocument.DefaultRootPartitionKey,
                new AggregateTypeStream<LoyaltyPoint>(),
                clientId,
                null,
                RetrieveEventOrder.NewToOld,
                20),
            m => eventsNewToOld.AddRange(m));
        Assert.Equal(20, eventsOldToNew.Count);
        Assert.Equal(20, eventsNewToOld.Count);
        Assert.NotEqual(eventsOldToNew[0], eventsNewToOld[0]);
        Assert.NotEqual(eventsOldToNew.First().Id, eventsNewToOld.Last().Id);
        Assert.True(eventsOldToNew.Last().Version < eventsNewToOld.First().Version);





        // check if all event order is correct
        eventsOldToNew = new List<IEvent>();
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(null, new AllStream(), null, null),
            m => eventsOldToNew.AddRange(m));
        eventsNewToOld = new List<IEvent>();
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(null, new AllStream(), null, null, RetrieveEventOrder.NewToOld),
            m => eventsNewToOld.AddRange(m));
        Assert.Equal(103, eventsOldToNew.Count);
        Assert.Equal(103, eventsNewToOld.Count);
        Assert.NotEqual(eventsOldToNew[0], eventsNewToOld[0]);
        Assert.Equal(eventsOldToNew.First().Id, eventsNewToOld.Last().Id);

        // check if all event order and max count is correct
        eventsOldToNew = new List<IEvent>();
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(null, new AllStream(), null, null, RetrieveEventOrder.OldToNew, 20),
            m => eventsOldToNew.AddRange(m));
        eventsNewToOld = new List<IEvent>();
        await repository.GetEvents(
            EventRetrievalInfo.FromNullableValues(null, new AllStream(), null, null, RetrieveEventOrder.NewToOld, 20),
            m => eventsNewToOld.AddRange(m));
        Assert.Equal(20, eventsOldToNew.Count);
        Assert.Equal(20, eventsNewToOld.Count);
        Assert.NotEqual(eventsOldToNew[0], eventsNewToOld[0]);
        Assert.NotEqual(eventsOldToNew.First().Id, eventsNewToOld.Last().Id);
        Assert.True(eventsOldToNew.Last().Version < eventsNewToOld.First().Version);

    }
}
