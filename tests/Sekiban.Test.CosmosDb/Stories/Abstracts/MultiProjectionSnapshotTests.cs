using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Projections;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointMultiples;
using FeatureCheck.Domain.Shared;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Snapshot.BackgroundServices;
using Sekiban.Testing.Story;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories.Abstracts;

public abstract class MultiProjectionSnapshotTests : TestBase<FeatureCheckDependency>
{
    private readonly MultiProjectionCollectionGenerator _multiProjectionCollectionGenerator;
    private readonly IMultiProjectionSnapshotGenerator multiProjectionSnapshotGenerator;
    public MultiProjectionSnapshotTests(
        SekibanTestFixture sekibanTestFixture,
        ITestOutputHelper testOutputHelper,
        ISekibanServiceProviderGenerator providerGenerator) : base(sekibanTestFixture, testOutputHelper, providerGenerator)
    {
        multiProjectionSnapshotGenerator = GetService<IMultiProjectionSnapshotGenerator>();
        _multiProjectionCollectionGenerator = GetService<MultiProjectionCollectionGenerator>();
    }


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

    [Fact]
    public async Task MultiProjectionSnapshotCollectionTest()
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
        // need to wait until safe state
        await Task.Delay(5000);
        ResetInMemoryDocumentStoreAndCache();

        await _multiProjectionCollectionGenerator.GenerateAsync(new FeatureCheckMultiProjectionSnapshotSettings());

        var projection = await multiProjectionService.GetMultiProjectionAsync<ClientLoyaltyPointListProjection>();
        Assert.True(projection.AppliedSnapshotVersion > 0);

        var projection2 = await multiProjectionService.GetMultiProjectionAsync<ClientLoyaltyPointMultiProjection>();
        Assert.True(projection2.AppliedSnapshotVersion > 0);

        var listProjection = await multiProjectionService.GetAggregateListObject<Client>(null);
        Assert.True(listProjection.AppliedSnapshotVersion > 0);

        var singleProjectionList = await multiProjectionService.GetSingleProjectionListObject<ClientNameHistoryProjection>(null);
        Assert.True(singleProjectionList.AppliedSnapshotVersion > 0);
    }
    [Fact]
    public async Task MultiProjectionSnapshotCollectionAllTest()
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
        // need to wait until safe state
        await Task.Delay(5000);
        ResetInMemoryDocumentStoreAndCache();

        await _multiProjectionCollectionGenerator.GenerateAsync(new FeatureCheckMultiProjectionAllSnapshotSettings());

        var projection = await multiProjectionService.GetMultiProjectionAsync<ClientLoyaltyPointListProjection>();
        Assert.True(projection.AppliedSnapshotVersion > 0);

        var projection2 = await multiProjectionService.GetMultiProjectionAsync<ClientLoyaltyPointMultiProjection>();
        Assert.True(projection2.AppliedSnapshotVersion > 0);

        var listProjection = await multiProjectionService.GetAggregateListObject<Client>(null);
        Assert.True(listProjection.AppliedSnapshotVersion > 0);

        var singleProjectionList = await multiProjectionService.GetSingleProjectionListObject<ClientNameHistoryProjection>(null);
        Assert.True(singleProjectionList.AppliedSnapshotVersion > 0);
    }

    [Fact]
    public void MultiProjectionSnapshotCollectionConfigurationTest()
    {
        var settings = new FeatureCheckMultiProjectionSnapshotConfigurationSetting(configuration);
        Assert.Equal(30, settings.GetMinimumNumberOfEventsToGenerateSnapshot());
    }

    [Fact]
    public void GenerateSettingByReflectionTest()
    {
        var setting1 = MultiProjectionSnapshotCollectionBackgroundService<FeatureCheckMultiProjectionAllSnapshotSettings>.GetSetting(configuration);
        Assert.NotNull(setting1);

        var setting2
            = MultiProjectionSnapshotCollectionBackgroundService<FeatureCheckMultiProjectionSnapshotConfigurationSetting>.GetSetting(configuration);
        Assert.NotNull(setting2);
        Assert.Equal(30, setting2.GetMinimumNumberOfEventsToGenerateSnapshot());

        var setting3 = MultiProjectionSnapshotCollectionBackgroundService<FeatureCheckMultiProjectionSnapshotSettings>.GetSetting(configuration);
        Assert.NotNull(setting3);
    }
}
