using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Projections;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;
using FeatureCheck.Domain.Shared;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Testing.Story;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories.Abstracts;

public abstract class MultiProjectionSnapshotTests : TestBase<FeatureCheckDependency>
{
    private readonly IMultiProjectionSnapshotGenerator multiProjectionSnapshotGenerator;
    public MultiProjectionSnapshotTests(
        SekibanTestFixture sekibanTestFixture,
        ITestOutputHelper testOutputHelper,
        ISekibanServiceProviderGenerator providerGenerator) : base(sekibanTestFixture, testOutputHelper, providerGenerator) =>
        multiProjectionSnapshotGenerator = GetService<IMultiProjectionSnapshotGenerator>();


    [Fact]
    public async Task MultiProjectionTest()
    {
        RemoveAllFromDefaultAndDissolvable();

        var response = await commandExecutor.ExecCommandAsync(new CreateBranch("branch1"));
        var branchId = response.AggregateId!.Value;
        response = await commandExecutor.ExecCommandAsync(new CreateClient(branchId, "client name", "client@example.com"));
        var clientId = response.AggregateId!.Value;
        foreach (var number in Enumerable.Range(1, 50))
        {
            response = await commandExecutor.ExecCommandAsync(
                new ChangeClientName(branchId, $"client name {number}") { ClientId = clientId, ReferenceVersion = response.Version });
        }
        var pointAggregate = await aggregateLoader.AsDefaultStateAsync<LoyaltyPoint>(clientId);
        var pointVersion = pointAggregate!.Version;
        foreach (var number in Enumerable.Range(1, 50))
        {
            response = await commandExecutor.ExecCommandAsync(
                new AddLoyaltyPoint(clientId, DateTime.Now, LoyaltyPointReceiveTypeKeys.CreditcardUsage, number, $"note {number}")
                {
                    ReferenceVersion = pointVersion
                });
            pointVersion = response.Version;
        }
        await Task.Delay(5000);
        var projection = await multiProjectionSnapshotGenerator.GenerateMultiProjectionSnapshotAsync<ClientLoyaltyPointListProjection>(50);
        Assert.True(projection.Version > 100);

        ResetInMemoryDocumentStoreAndCache();

        projection = await multiProjectionService.GetMultiProjectionAsync<ClientLoyaltyPointListProjection>();
        Assert.True(projection.AppliedSnapshotVersion > 0);

        var listProjection = await multiProjectionSnapshotGenerator.GenerateAggregateListSnapshotAsync<Client>(50);
        Assert.True(listProjection.Version > 50);

        ResetInMemoryDocumentStoreAndCache();

        listProjection = await multiProjectionService.GetAggregateListObject<Client>(null);
        Assert.True(listProjection.AppliedSnapshotVersion > 0);


        var listProjection2 = await multiProjectionSnapshotGenerator.GenerateSingleProjectionListSnapshotAsync<ClientNameHistoryProjection>(50);
        Assert.True(listProjection2.Version > 50);

        ResetInMemoryDocumentStoreAndCache();

        listProjection2 = await multiProjectionService.GetSingleProjectionListObject<ClientNameHistoryProjection>(null);
        Assert.True(listProjection2.AppliedSnapshotVersion > 0);

    }
}
