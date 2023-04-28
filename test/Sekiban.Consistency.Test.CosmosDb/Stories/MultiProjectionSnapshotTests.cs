using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Projections;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;
using Microsoft.Extensions.Caching.Memory;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Cache;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.MultiProjections.Projections;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Query.SingleProjections.Projections;
using Sekiban.Core.Setting;
using Sekiban.Infrastructure.Cosmos;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest.Stories;

public class MultiProjectionSnapshotTests : TestBase
{


    private readonly CosmosDbFactory _cosmosDbFactory;
    private readonly IDocumentPersistentRepository _documentPersistentRepository;
    private readonly IDocumentPersistentWriter _documentPersistentWriter;
    private readonly HybridStoreManager _hybridStoreManager;
    private readonly InMemoryDocumentStore _inMemoryDocumentStore;
    private readonly IMemoryCacheAccessor _memoryCache;
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly IAggregateLoader aggregateLoader;
    private readonly IBlobAccessor blobAccessor;
    private readonly ICommandExecutor commandExecutor;
    private readonly IMultiProjectionService multiProjectionService;
    private readonly IMultiProjectionSnapshotGenerator multiProjectionSnapshotGenerator;
    private readonly ISingleProjectionSnapshotAccessor singleProjectionSnapshotAccessor;
    public MultiProjectionSnapshotTests(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _cosmosDbFactory = GetService<CosmosDbFactory>();
        commandExecutor = GetService<ICommandExecutor>();
        aggregateLoader = GetService<IAggregateLoader>();
        _documentPersistentRepository = GetService<IDocumentPersistentRepository>();
        multiProjectionService = GetService<IMultiProjectionService>();
        _inMemoryDocumentStore = GetService<InMemoryDocumentStore>();
        _hybridStoreManager = GetService<HybridStoreManager>();
        _memoryCache = GetService<IMemoryCacheAccessor>();
        singleProjectionSnapshotAccessor = GetService<ISingleProjectionSnapshotAccessor>();
        _documentPersistentWriter = GetService<IDocumentPersistentWriter>();
        blobAccessor = GetService<IBlobAccessor>();
        multiProjectionSnapshotGenerator = GetService<IMultiProjectionSnapshotGenerator>();
    }


    [Fact]
    public async Task MultiProjectionTest()
    {
        // 先に全データを削除する
        await _cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Default);
        await _cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(
            DocumentType.Command,
            AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.Command);

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
                    { ReferenceVersion = pointVersion });
            pointVersion = response.Version;
        }
        await Task.Delay(5000);
        var projection = await multiProjectionSnapshotGenerator
            .GenerateMultiProjectionSnapshotAsync<ClientLoyaltyPointListProjection>(50);
        Assert.True(projection.Version > 100);

        // Remove in memory data
        _inMemoryDocumentStore.ResetInMemoryStore();
        _hybridStoreManager.ClearHybridPartitions();
        ((MemoryCache)_memoryCache.Cache).Compact(1);

        projection = await multiProjectionService.GetMultiProjectionAsync<ClientLoyaltyPointListProjection>();
        Assert.True(projection.AppliedSnapshotVersion > 0);

        var listProjection = await multiProjectionSnapshotGenerator
            .GenerateAggregateListSnapshotAsync<Client>(50);
        Assert.True(listProjection.Version > 50);

        // Remove in memory data
        _inMemoryDocumentStore.ResetInMemoryStore();
        _hybridStoreManager.ClearHybridPartitions();
        ((MemoryCache)_memoryCache.Cache).Compact(1);

        listProjection = await multiProjectionService.GetAggregateListObject<Client>(null);
        Assert.True(listProjection.AppliedSnapshotVersion > 0);


        var listProjection2 = await multiProjectionSnapshotGenerator
            .GenerateSingleProjectionListSnapshotAsync<ClientNameHistoryProjection>(50);
        Assert.True(listProjection2.Version > 50);

        // Remove in memory data
        _inMemoryDocumentStore.ResetInMemoryStore();
        _hybridStoreManager.ClearHybridPartitions();
        ((MemoryCache)_memoryCache.Cache).Compact(1);

        listProjection2 = await multiProjectionService.GetSingleProjectionListObject<ClientNameHistoryProjection>(null);
        Assert.True(listProjection2.AppliedSnapshotVersion > 0);

    }
}
