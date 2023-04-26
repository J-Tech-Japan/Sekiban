using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Projections;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
using FeatureCheck.Domain.Aggregates.RecentActivities;
using FeatureCheck.Domain.Aggregates.RecentActivities.Commands;
using FeatureCheck.Domain.Aggregates.RecentActivities.Projections;
using FeatureCheck.Domain.Aggregates.RecentInMemoryActivities;
using FeatureCheck.Domain.Aggregates.RecentInMemoryActivities.Commands;
using FeatureCheck.Domain.Projections.ClientLoyaltyPointMultiples;
using FeatureCheck.Domain.Shared.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Cache;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Snapshot;
using Sekiban.Core.Snapshot.Aggregate;
using Sekiban.Core.Types;
using Sekiban.Infrastructure.Cosmos;
using Sekiban.Testing.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest.Stories;

public class CustomerDbStoryBasic : TestBase
{
    private readonly CosmosDbFactory _cosmosDbFactory;
    private readonly IDocumentPersistentRepository _documentPersistentRepository;
    private readonly HybridStoreManager _hybridStoreManager;
    private readonly InMemoryDocumentStore _inMemoryDocumentStore;
    private readonly IMemoryCacheAccessor _memoryCache;
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly ICommandExecutor commandExecutor;
    private readonly IMultiProjectionService multiProjectionService;
    private readonly IAggregateLoader projectionService;

    public CustomerDbStoryBasic(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _cosmosDbFactory = GetService<CosmosDbFactory>();
        commandExecutor = GetService<ICommandExecutor>();
        projectionService = GetService<IAggregateLoader>();
        _documentPersistentRepository = GetService<IDocumentPersistentRepository>();
        multiProjectionService = GetService<IMultiProjectionService>();
        _inMemoryDocumentStore = GetService<InMemoryDocumentStore>();
        _hybridStoreManager = GetService<HybridStoreManager>();
        _memoryCache = GetService<IMemoryCacheAccessor>();
    }

    [Fact(DisplayName = "CosmosDb ストーリーテスト 集約の機能のテストではなく、CosmosDbと連携して正しく動くかをテストしています。")]
    public async Task CosmosDbStory()
    {
        // 先に全データを削除する
        await _cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Default);
        await _cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(
            DocumentType.Command,
            AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.Command);

        // create list branch
        var branchList = await multiProjectionService.GetAggregateList<Branch>();
        Assert.Empty(branchList);
        var branchResult
            = await commandExecutor.ExecCommandAsync(
                new CreateBranch("Japan"));
        var branchId = branchResult.AggregateId!.Value;
        Assert.NotNull(branchResult);
        Assert.NotNull(branchResult.AggregateId);
        branchList = await multiProjectionService.GetAggregateList<Branch>();
        Assert.Single(branchList);
        var branchFromList = branchList.First(m => m.AggregateId == branchId);
        Assert.NotNull(branchFromList);

        var branchResult2 =
            await commandExecutor.ExecCommandAsync(
                new CreateBranch("USA"));
        branchList = await multiProjectionService.GetAggregateList<Branch>();
        Assert.Equal(2, branchList.Count);
        var branchListFromMultiple = await multiProjectionService.GetAggregateList<Branch>();
        Assert.Equal(2, branchListFromMultiple.Count);

        // loyalty point should be []  
        var loyaltyPointList = await multiProjectionService.GetAggregateList<LoyaltyPoint>();
        Assert.Empty(loyaltyPointList);

        var clientNameList = await multiProjectionService
            .GetSingleProjectionList<ClientNameHistoryProjection>();
        Assert.Empty(clientNameList);

        // create client
        var clientList = await multiProjectionService.GetAggregateList<Client>();
        Assert.Empty(clientList);
        var originalName = "Tanaka Taro";
        var createClientResult = await commandExecutor.ExecCommandAsync(
            new CreateClient(branchId, originalName, "tanaka@example.com"));
        var clientId = createClientResult.AggregateId!.Value;
        Assert.NotNull(createClientResult);
        Assert.NotNull(createClientResult.AggregateId);
        clientList = await multiProjectionService.GetAggregateList<Client>();
        Assert.Single(clientList);

        // singleAggregateProjection
        clientNameList = await multiProjectionService
            .GetSingleProjectionList<ClientNameHistoryProjection>();
        Assert.Single(clientNameList);
        var tanakaProjection = clientNameList.First(m => m.AggregateId == clientId);
        Assert.Single(tanakaProjection.Payload.ClientNames);
        Assert.Equal(originalName, tanakaProjection.Payload.ClientNames.First().Name);

        var clientNameListFromMultiple = await multiProjectionService
            .GetSingleProjectionList<ClientNameHistoryProjection>();
        Assert.Single(clientNameListFromMultiple);
        Assert.Equal(clientNameList.First().AggregateId, clientNameListFromMultiple.First().AggregateId);

        var branchResult3
            = await commandExecutor.ExecCommandAsync(
                new CreateBranch("California"));
        branchList = await multiProjectionService.GetAggregateList<Branch>();

        var secondName = "田中 太郎";
        // should throw version error 
        await Assert.ThrowsAsync<SekibanCommandInconsistentVersionException>(
            async () =>
            {
                await commandExecutor.ExecCommandAsync(
                    new ChangeClientName(clientId, secondName));
            });
        // change name
        var changeNameResult = await commandExecutor.ExecCommandAsync(
            new ChangeClientName(clientId, secondName) { ReferenceVersion = createClientResult.Version });

        // change name projection
        clientNameList = await multiProjectionService
            .GetSingleProjectionList<ClientNameHistoryProjection>();
        Assert.Single(clientNameList);
        tanakaProjection = clientNameList.First(m => m.AggregateId == clientId);
        Assert.Equal(2, tanakaProjection.Payload.ClientNames.Count);
        Assert.Equal(originalName, tanakaProjection.Payload.ClientNames.First().Name);
        Assert.Equal(secondName, tanakaProjection.Payload.ClientNames.ToList()[1].Name);

        // test change name multiple time to create projection 
        var versionCN = changeNameResult.Version;
        var countChangeName = 160;
        foreach (var i in Enumerable.Range(0, countChangeName))
        {
            var changeNameResult2 = await commandExecutor.ExecCommandAsync(
                new ChangeClientName(clientId, $"newname - {i + 1}") { ReferenceVersion = versionCN });
            versionCN = changeNameResult2.Version;
        }

        // get change name state
        var changeNameProjection
            = await projectionService
                .AsSingleProjectionStateAsync<ClientNameHistoryProjection>(
                    clientId);
        Assert.NotNull(changeNameProjection);

        // loyalty point should be created with event subscribe
        loyaltyPointList = await multiProjectionService.GetAggregateList<LoyaltyPoint>();
        Assert.Single(clientList);
        // first point = 0
        var loyaltyPoint = await projectionService.AsDefaultStateAsync<LoyaltyPoint>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(0, loyaltyPoint!.Payload.CurrentPoint);

        var datetimeFirst = DateTime.Now;
        var addPointResult = await commandExecutor.ExecCommandAsync(
            new AddLoyaltyPoint(clientId, datetimeFirst, LoyaltyPointReceiveTypeKeys.FlightDomestic, 1000, "")
            {
                ReferenceVersion = loyaltyPoint.Version
            });
        Assert.NotNull(addPointResult);
        Assert.NotNull(addPointResult.AggregateId);

        loyaltyPoint = await projectionService.AsDefaultStateAsync<LoyaltyPoint>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(1000, loyaltyPoint!.Payload.CurrentPoint);

        // should throw not enough point error 
        await Assert.ThrowsAsync<SekibanLoyaltyPointNotEnoughException>(
            async () =>
            {
                await commandExecutor.ExecCommandAsync(
                    new UseLoyaltyPoint(
                        clientId,
                        datetimeFirst.AddSeconds(1),
                        LoyaltyPointUsageTypeKeys.FlightUpgrade,
                        2000,
                        "")
                    {
                        ReferenceVersion = addPointResult.Version
                    });
            });
        var usePointResult = await commandExecutor.ExecCommandAsync(
            new UseLoyaltyPoint(clientId, DateTime.Now, LoyaltyPointUsageTypeKeys.FlightUpgrade, 200, "")
            {
                ReferenceVersion = addPointResult.Version
            });
        Assert.NotNull(usePointResult);
        Assert.NotNull(usePointResult.AggregateId);

        loyaltyPoint = await projectionService.AsDefaultStateAsync<LoyaltyPoint>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(800, loyaltyPoint!.Payload.CurrentPoint);

        var p = await multiProjectionService
            .GetMultiProjectionAsync<ClientLoyaltyPointMultiProjection>();
        Assert.NotNull(p);
        Assert.Equal(3, p.Payload.Branches.Count);
        Assert.Single(p.Payload.Records);

        // delete client
        var deleteClientResult = await commandExecutor.ExecCommandAsync(
            new DeleteClient(clientId) { ReferenceVersion = versionCN });
        Assert.NotNull(deleteClientResult);
        Assert.NotNull(deleteClientResult.AggregateId);
        // client deleted
        clientList = await multiProjectionService.GetAggregateList<Client>();
        Assert.Empty(clientList);
        // can find deleted client
        clientList = await multiProjectionService.GetAggregateList<Client>(null, QueryListType.DeletedOnly);
        Assert.Single(clientList);
        clientList = await multiProjectionService.GetAggregateList<Client>(null, QueryListType.ActiveAndDeleted);
        Assert.Single(clientList);

        // loyalty point should be created with event subscribe
        loyaltyPointList = await multiProjectionService.GetAggregateList<LoyaltyPoint>();
        Assert.Empty(loyaltyPointList);
        loyaltyPointList = await multiProjectionService.GetAggregateList<LoyaltyPoint>(null, QueryListType.DeletedOnly);
        Assert.Single(loyaltyPointList);

        // create recent activity
        var createRecentActivityResult
            = await commandExecutor
                .ExecCommandAsync(
                    new CreateRecentActivity());

        var recentActivityList = await multiProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);
        var version = createRecentActivityResult.Version;
        var count = 160;
        foreach (var i in Enumerable.Range(0, count))
        {
            var recentActivityAddedResult
                = await commandExecutor.ExecCommandAsync(
                    new AddRecentActivity(createRecentActivityResult.AggregateId!.Value, $"Message - {i + 1}")
                        { ReferenceVersion = version });
            version = recentActivityAddedResult.Version;
        }

        recentActivityList = await multiProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);
        Assert.Equal(count + 1, version);

        // only publish event run

        var recentActivityId = createRecentActivityResult.AggregateId!.Value;

        await commandExecutor.ExecCommandAsync(
            new OnlyPublishingAddRecentActivity(recentActivityId, "only publish event"));

        // get single aggregate and applied event
        var recentActivityState = await projectionService.AsDefaultStateAsync<RecentActivity>(recentActivityId);
        Assert.Equal("only publish event", recentActivityState?.Payload.LatestActivities.First().Activity);

        p = await multiProjectionService
            .GetMultiProjectionAsync<ClientLoyaltyPointMultiProjection>();
        Assert.NotNull(p);
        Assert.Equal(3, p.Payload.Branches.Count);
        Assert.Empty(p.Payload.Records);
        var snapshotManager
            = await projectionService.AsDefaultStateFromInitialAsync<SnapshotManager>(
                SnapshotManager.SharedId);
        if (snapshotManager is null)
        {
            _testOutputHelper.WriteLine("snapshot manager is null");
        }
        else
        {
            _testOutputHelper.WriteLine("-requests-");
            foreach (var key in snapshotManager!.Payload.Requests)
            {
                _testOutputHelper.WriteLine(key);
            }
            _testOutputHelper.WriteLine("-request takens-");
            foreach (var key in snapshotManager!.Payload.RequestTakens)
            {
                _testOutputHelper.WriteLine(key);
            }
        }

        branchList = await multiProjectionService.GetAggregateList<Branch>();
        Assert.Equal(3, branchList.Count);
    }

    [Fact]
    public async Task SnapshotTestAsync()
    {
        await DeleteonlyAsync();
        var branchResult = await commandExecutor.ExecCommandAsync(new CreateBranch("Tokyo"));
        var branchId = branchResult.AggregateId!.Value;
        var clientResult = await commandExecutor.ExecCommandAsync(new CreateClient(branchId, "name", "email@example.com"));
        var currentVersion = clientResult.Version;
        foreach (var i in Enumerable.Range(0, 160))
        {
            clientResult = await commandExecutor.ExecCommandAsync(
                new ChangeClientName(clientResult.AggregateId!.Value, $"name{i + 1}") { ReferenceVersion = currentVersion });
            currentVersion = clientResult.Version;
        }

        _inMemoryDocumentStore.ResetInMemoryStore();
        _hybridStoreManager.ClearHybridPartitions();
        ((MemoryCache)_memoryCache.Cache).Compact(1);

        var client = await projectionService.AsDefaultStateAsync<Client>(clientResult.AggregateId!.Value);
        Assert.NotNull(client);
        var clientProjection = await projectionService.AsSingleProjectionStateAsync<ClientNameHistoryProjection>(clientResult.AggregateId!.Value);
        Assert.NotNull(clientProjection);
    }

    [Fact(DisplayName = "CosmosDbストーリーテスト用に削除のみを行う 。")]
    public async Task DeleteonlyAsync()
    {
        // 先に全データを削除する
        await _cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Default);
        await _cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(
            DocumentType.Command,
            AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.Command);
    }

    [Trait(SekibanTestConstants.Category, SekibanTestConstants.Categories.Flaky)]
    [Fact(DisplayName = "No Flaky Test For now. This is just empty test")]
    public void NoFlakyTest()
    {
    }

    [Fact(DisplayName = "CosmosDb ストーリーテスト 。並列でたくさん動かしたらどうなるか。 INoValidateCommand がRecentActivityに適応されているので、問題ないはず")]
    [Trait(SekibanTestConstants.Category, SekibanTestConstants.Categories.Flaky)]
    public async Task AsynchronousExecutionTestAsync()
    {
        // 先に全データを削除する
        await _cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Default);
        await _cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(
            DocumentType.Command,
            AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.Command);

        // create recent activity
        var createRecentActivityResult
            = await commandExecutor
                .ExecCommandAsync(
                    new CreateRecentActivity());
        var recentActivityId = createRecentActivityResult.AggregateId;

        var recentActivityList = await multiProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);
        var version = createRecentActivityResult.Version;
        var tasks = new List<Task>();
        var count = 180;
        foreach (var i in Enumerable.Range(0, count))
        {
            tasks.Add(
                Task.Run(
                    async () =>
                    {
                        var recentActivityAddedResult
                            = await commandExecutor.ExecCommandAsync(
                                new AddRecentActivity(
                                    createRecentActivityResult.AggregateId!.Value,
                                    $"Message - {i + 1}")
                                {
                                    ReferenceVersion = version
                                });
                        version = recentActivityAddedResult.Version;
                    }));
        }
        await Task.WhenAll(tasks);
        recentActivityList = await multiProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);
        var projection = await projectionService.AsSingleProjectionStateAsync<TenRecentProjection>(createRecentActivityResult.AggregateId!.Value);
        Assert.NotNull(projection);
        // this works
        var aggregateRecentActivity
            = await projectionService.AsDefaultStateFromInitialAsync<RecentActivity>(
                createRecentActivityResult.AggregateId!.Value);
        var aggregateRecentActivity2
            = await projectionService.AsDefaultStateAsync<RecentActivity>(
                createRecentActivityResult.AggregateId!
                    .Value);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + 1, aggregateRecentActivity!.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);

        var snapshotManager
            = await projectionService.AsDefaultStateFromInitialAsync<SnapshotManager>(
                SnapshotManager.SharedId);
        _testOutputHelper.WriteLine("-requests-");
        foreach (var key in snapshotManager!.Payload.Requests)
        {
            _testOutputHelper.WriteLine(key);
        }
        _testOutputHelper.WriteLine("-request takens-");
        foreach (var key in snapshotManager!.Payload.RequestTakens)
        {
            _testOutputHelper.WriteLine(key);
        }

        var snapshots = await _documentPersistentRepository.GetSnapshotsForAggregateAsync(
            createRecentActivityResult.AggregateId!.Value,
            typeof(RecentActivity),
            typeof(RecentActivity));

        await CheckSnapshots<RecentActivity>(snapshots, createRecentActivityResult.AggregateId!.Value);

        var snapshots2 = await _documentPersistentRepository.GetSnapshotsForAggregateAsync(
            createRecentActivityResult.AggregateId!.Value,
            typeof(RecentActivity),
            typeof(TenRecentProjection));

        await CheckProjectionSnapshots<TenRecentProjection>(snapshots2, createRecentActivityResult.AggregateId!.Value);

        await _documentPersistentRepository.GetSnapshotsForAggregateAsync(
            createRecentActivityResult.AggregateId!.Value,
            typeof(RecentActivity),
            typeof(TenRecentProjection));

        _inMemoryDocumentStore.ResetInMemoryStore();
        _hybridStoreManager.ClearHybridPartitions();
        ((MemoryCache)_memoryCache.Cache).Compact(1);
        await ContinuousExecutionTestAsync();
    }

    private async Task CheckSnapshots<TAggregatePayload>(List<SnapshotDocument> snapshots, Guid aggregateId)
        where TAggregatePayload : IAggregatePayloadCommon
    {
        _testOutputHelper.WriteLine($"snapshots {typeof(TAggregatePayload).Name} {snapshots.Count} ");
        foreach (var snapshot in snapshots)
        {
            _testOutputHelper.WriteLine($"snapshot {snapshot.AggregateTypeName}  {snapshot.Id}  {snapshot.SavedVersion} is checking");
            var state = snapshot.ToState<AggregateState<TAggregatePayload>>();
            if (state is null)
            {
                _testOutputHelper.WriteLine($"Snapshot {snapshot.AggregateTypeName} {snapshot.Id} {snapshot.SavedVersion}  is null");
                throw new SekibanInvalidArgumentException($"Snapshot {snapshot.AggregateTypeName} {snapshot.SavedVersion}  is null");
            }
            _testOutputHelper.WriteLine($"Snapshot {snapshot.AggregateTypeName}  {snapshot.Id}  {snapshot.SavedVersion}  is not null");
            var fromInitial =
                await projectionService.AsDefaultStateFromInitialAsync<TAggregatePayload>(aggregateId, state.Version);
            if (fromInitial is null)
            {
                throw new SekibanInvalidArgumentException();
            }
            Assert.Equal(fromInitial.Version, state.Version);
            Assert.Equal(fromInitial.LastEventId, state.LastEventId);
        }
    }

    private async Task CheckProjectionSnapshots<TAggregatePayload>(List<SnapshotDocument> snapshots, Guid aggregateId)
        where TAggregatePayload : ISingleProjectionPayloadCommon, new()
    {
        var aggregateType = typeof(TAggregatePayload).GetOriginalTypeFromSingleProjectionPayload();
        _testOutputHelper.WriteLine($"snapshots {typeof(TAggregatePayload).Name} {snapshots.Count} ");
        foreach (var snapshot in snapshots)
        {
            _testOutputHelper.WriteLine(
                $"snapshot {snapshot.AggregateTypeName} {snapshot.DocumentTypeName} {snapshot.Id}  {snapshot.SavedVersion} is checking");
            var state = snapshot.ToState<AggregateState<TAggregatePayload>>();
            if (state is null)
            {
                _testOutputHelper.WriteLine(
                    $"Snapshot {snapshot.AggregateTypeName} {snapshot.DocumentTypeName} {snapshot.Id} {snapshot.SavedVersion}  is null");
                throw new SekibanInvalidArgumentException($"Snapshot {snapshot.AggregateTypeName} {snapshot.SavedVersion}  is null");
            }
            _testOutputHelper.WriteLine(
                $"Snapshot {snapshot.AggregateTypeName} {snapshot.DocumentTypeName} {snapshot.Id}  {snapshot.SavedVersion}  is not null");
            var fromInitial =
                await projectionService.AsSingleProjectionStateFromInitialAsync<TAggregatePayload>(aggregateId, state.Version);
            if (fromInitial is null)
            {
                throw new SekibanInvalidArgumentException();
            }
            Assert.Equal(fromInitial.Version, state.Version);
            Assert.Equal(fromInitial.LastEventId, state.LastEventId);
        }
    }

    [Fact(DisplayName = "インメモリストーリーテスト 。並列でたくさん動かしたらどうなるか。 Versionの重複が発生しないことを確認")]
    public async Task AsynchronousInMemoryExecutionTestAsync()
    {
        // create recent activity
        var createRecentActivityResult
            = await commandExecutor
                .ExecCommandAsync(
                    new CreateRecentInMemoryActivity());

        var recentActivityList = await multiProjectionService.GetAggregateList<RecentInMemoryActivity>();
        Assert.Single(recentActivityList);
        var version = createRecentActivityResult.Version;
        var tasks = new List<Task>();
        var count = 140;
        foreach (var i in Enumerable.Range(0, count))
        {
            tasks.Add(
                Task.Run(
                    async () =>
                    {
                        var recentActivityAddedResult
                            = await commandExecutor
                                .ExecCommandAsync(
                                    new AddRecentInMemoryActivity(
                                        createRecentActivityResult.AggregateId!.Value,
                                        $"Message - {i + 1}")
                                    {
                                        ReferenceVersion = version
                                    });
                        version = recentActivityAddedResult.Version;
                        _testOutputHelper.WriteLine($"{i} - {recentActivityAddedResult.Version.ToString()}");
                    }));
        }
        await Task.WhenAll(tasks);
        recentActivityList = await multiProjectionService.GetAggregateList<RecentInMemoryActivity>();
        Assert.Single(recentActivityList);

        var aggregateRecentActivity
            = await projectionService.AsDefaultStateFromInitialAsync<RecentInMemoryActivity>(
                createRecentActivityResult.AggregateId!.Value);
        var aggregateRecentActivity2
            = await projectionService.AsDefaultStateAsync<RecentInMemoryActivity>(
                createRecentActivityResult.AggregateId!.Value);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + 1, aggregateRecentActivity!.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);
    }

    private async Task ContinuousExecutionTestAsync()
    {
        _testOutputHelper.WriteLine("481");
        var recentActivityList = await multiProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);
        var aggregateId = recentActivityList.First().AggregateId;

        var aggregate = await projectionService.AsAggregateAsync<RecentActivity>(aggregateId);
        Assert.NotNull(aggregate);
        var _ = await projectionService.AsDefaultStateAsync<RecentActivity>(aggregateId);

        //var aggregateRecentActivity =
        //    await projectionService
        //        .AsSingleProjectionStateFromInitialAsync<CreateRecentActivity>(
        //            aggregateId);
        //Assert.Single(recentActivityList);
        //Assert.NotNull(aggregateRecentActivity);
        //Assert.NotNull(aggregateRecentActivity2);
        //Assert.Equal(aggregateRecentActivity!.Version, aggregateRecentActivity2!.Version);

        _testOutputHelper.WriteLine("498");
        var version = recentActivityList.First().Version;
        var tasks = new List<Task>();
        var count = 180;
        foreach (var i in Enumerable.Range(0, count))
        {
            tasks.Add(
                Task.Run(
                    async () =>
                    {
                        var recentActivityAddedResult
                            = await commandExecutor.ExecCommandAsync(
                                new AddRecentActivity(aggregateId, $"Message - {i + 1}")
                                    { ReferenceVersion = version });
                        version = recentActivityAddedResult.Version;
                    }));
        }
        await Task.WhenAll(tasks);
        recentActivityList = await multiProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);

        _testOutputHelper.WriteLine("518");

        var snapshots =
            await _documentPersistentRepository.GetSnapshotsForAggregateAsync(aggregateId, typeof(RecentActivity), typeof(RecentActivity));
        await CheckSnapshots<RecentActivity>(snapshots, aggregateId);
        var projectionSnapshots =
            await _documentPersistentRepository.GetSnapshotsForAggregateAsync(aggregateId, typeof(RecentActivity), typeof(TenRecentProjection));
        Assert.NotEmpty(projectionSnapshots);

        await CheckProjectionSnapshots<TenRecentProjection>(projectionSnapshots, aggregateId);

        // check aggregate result
        var aggregateRecentActivity
            = await projectionService.AsDefaultStateFromInitialAsync<RecentActivity>(aggregateId);
        var aggregateRecentActivity2 = await projectionService.AsDefaultStateAsync<RecentActivity>(aggregateId);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + aggregate!.ToState().Version, aggregateRecentActivity!.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);

        var snapshotManager
            = await projectionService.AsDefaultStateFromInitialAsync<SnapshotManager>(
                SnapshotManager.SharedId);
        _testOutputHelper.WriteLine("-requests-");
        foreach (var key in snapshotManager!.Payload.Requests)
        {
            _testOutputHelper.WriteLine(key);
        }
        _testOutputHelper.WriteLine("-request takens-");
        foreach (var key in snapshotManager!.Payload.RequestTakens)
        {
            _testOutputHelper.WriteLine(key);
        }
    }
}
