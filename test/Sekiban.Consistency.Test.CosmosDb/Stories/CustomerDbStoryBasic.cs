using Customer.Domain.Aggregates.Branches;
using Customer.Domain.Aggregates.Branches.Commands;
using Customer.Domain.Aggregates.Clients;
using Customer.Domain.Aggregates.Clients.Commands;
using Customer.Domain.Aggregates.Clients.Projections;
using Customer.Domain.Aggregates.LoyaltyPoints;
using Customer.Domain.Aggregates.LoyaltyPoints.Commands;
using Customer.Domain.Aggregates.LoyaltyPoints.Consts;
using Customer.Domain.Aggregates.RecentActivities;
using Customer.Domain.Aggregates.RecentActivities.Commands;
using Customer.Domain.Aggregates.RecentInMemoryActivities;
using Customer.Domain.Aggregates.RecentInMemoryActivities.Commands;
using Customer.Domain.Projections.ClientLoyaltyPointMultiples;
using Customer.Domain.Shared.Exceptions;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Document;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.SingleAggregate;
using Sekiban.Core.Snapshot;
using Sekiban.Core.Snapshot.Aggregate;
using Sekiban.Infrastructure.Cosmos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest.Stories;

public class CustomerDbStoryBasic : TestBase
{
    private readonly IAggregateCommandExecutor _aggregateCommandExecutor;
    private readonly ISingleAggregateService _aggregateService;
    private readonly CosmosDbFactory _cosmosDbFactory;
    private readonly IDocumentPersistentRepository _documentPersistentRepository;
    private readonly HybridStoreManager _hybridStoreManager;
    private readonly InMemoryDocumentStore _inMemoryDocumentStore;
    private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    private readonly ITestOutputHelper _testOutputHelper;
    public CustomerDbStoryBasic(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(sekibanTestFixture)
    {
        _testOutputHelper = testOutputHelper;
        _cosmosDbFactory = GetService<CosmosDbFactory>();
        _aggregateCommandExecutor = GetService<IAggregateCommandExecutor>();
        _aggregateService = GetService<ISingleAggregateService>();
        _documentPersistentRepository = GetService<IDocumentPersistentRepository>();
        _multipleAggregateProjectionService = GetService<IMultipleAggregateProjectionService>();
        _inMemoryDocumentStore = GetService<InMemoryDocumentStore>();
        _hybridStoreManager = GetService<HybridStoreManager>();
    }
    [Fact(DisplayName = "CosmosDb ストーリーテスト 集約の機能のテストではなく、CosmosDbと連携して正しく動くかをテストしています。")]
    public async Task CosmosDbStory()
    {
        // 先に全データを削除する
        await _cosmosDbFactory.DeleteAllFromAggregateEventContainer(AggregateContainerGroup.Default);
        await _cosmosDbFactory.DeleteAllFromAggregateEventContainer(AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.AggregateCommand, AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.AggregateCommand);

        // create list branch
        var branchList = await _multipleAggregateProjectionService.GetAggregateList<Branch>();
        Assert.Empty(branchList);
        var (branchResult, _)
            = await _aggregateCommandExecutor.ExecCreateCommandAsync<Branch, CreateBranch>(new CreateBranch("Japan"));
        var branchId = branchResult.AggregateId!.Value;
        Assert.NotNull(branchResult);
        Assert.NotNull(branchResult.AggregateId);
        branchList = await _multipleAggregateProjectionService.GetAggregateList<Branch>();
        Assert.Single(branchList);
        var branchFromList = branchList.First(m => m.AggregateId == branchId);
        Assert.NotNull(branchFromList);

        var branchResult2 = await _aggregateCommandExecutor.ExecCreateCommandAsync<Branch, CreateBranch>(new CreateBranch("USA"));
        branchList = await _multipleAggregateProjectionService.GetAggregateList<Branch>();
        Assert.Equal(2, branchList.Count);
        var branchListFromMultiple = await _multipleAggregateProjectionService.GetAggregateList<Branch>();
        Assert.Equal(2, branchListFromMultiple.Count);

        // loyalty point should be []  
        var loyaltyPointList = await _multipleAggregateProjectionService.GetAggregateList<LoyaltyPoint>();
        Assert.Empty(loyaltyPointList);

        var clientNameList = await _multipleAggregateProjectionService
            .GetSingleAggregateProjectionList<Client,ClientNameHistoryProjection, ClientNameHistoryProjection.PayloadDefinition>();
        Assert.Empty(clientNameList);

        // create client
        var clientList = await _multipleAggregateProjectionService.GetAggregateList<Client>();
        Assert.Empty(clientList);
        var originalName = "Tanaka Taro";
        var (createClientResult, _) = await _aggregateCommandExecutor.ExecCreateCommandAsync<Client, CreateClient>(
            new CreateClient(branchId, originalName, "tanaka@example.com"));
        var clientId = createClientResult.AggregateId!.Value;
        Assert.NotNull(createClientResult);
        Assert.NotNull(createClientResult.AggregateId);
        clientList = await _multipleAggregateProjectionService.GetAggregateList<Client>();
        Assert.Single(clientList);

        // singleAggregateProjection
        clientNameList = await _multipleAggregateProjectionService
            .GetSingleAggregateProjectionList<Client,ClientNameHistoryProjection, ClientNameHistoryProjection.PayloadDefinition>();
        Assert.Single(clientNameList);
        var tanakaProjection = clientNameList.First(m => m.AggregateId == clientId);
        Assert.Single(tanakaProjection.Payload.ClientNames);
        Assert.Equal(originalName, tanakaProjection.Payload.ClientNames.First().Name);

        var clientNameListFromMultiple = await _multipleAggregateProjectionService
            .GetSingleAggregateProjectionList<Client,ClientNameHistoryProjection, ClientNameHistoryProjection.PayloadDefinition>();
        Assert.Single(clientNameListFromMultiple);
        Assert.Equal(clientNameList.First().AggregateId, clientNameListFromMultiple.First().AggregateId);

        var branchResult3
            = await _aggregateCommandExecutor.ExecCreateCommandAsync<Branch, CreateBranch>(new CreateBranch("California"));
        branchList = await _multipleAggregateProjectionService.GetAggregateList<Branch>();

        var secondName = "田中 太郎";
        // should throw version error 
        await Assert.ThrowsAsync<SekibanAggregateCommandInconsistentVersionException>(
            async () =>
            {
                await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ChangeClientName>(
                    new ChangeClientName(clientId, secondName));
            });
        // change name
        var (changeNameResult, _) = await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ChangeClientName>(
            new ChangeClientName(clientId, secondName) { ReferenceVersion = createClientResult.Version });

        // change name projection
        clientNameList = await _multipleAggregateProjectionService
            .GetSingleAggregateProjectionList<Client,ClientNameHistoryProjection, ClientNameHistoryProjection.PayloadDefinition>();
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
            var (changeNameResult2, _) = await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ChangeClientName>(
                new ChangeClientName(clientId, $"newname - {i + 1}") { ReferenceVersion = versionCN });
            versionCN = changeNameResult2.Version;
        }

        // get change name dto
        var changeNameProjection
            = await _aggregateService.GetProjectionAsync<Client,ClientNameHistoryProjection, ClientNameHistoryProjection.PayloadDefinition>(
                clientId);
        Assert.NotNull(changeNameProjection);

        // loyalty point should be created with event subscribe
        loyaltyPointList = await _multipleAggregateProjectionService.GetAggregateList<LoyaltyPoint>();
        Assert.Single(clientList);
        // first point = 0
        var loyaltyPoint = await _aggregateService.GetAggregateStateAsync<LoyaltyPoint>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(0, loyaltyPoint!.Payload.CurrentPoint);

        var datetimeFirst = DateTime.Now;
        var (addPointResult, _) = await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, AddLoyaltyPoint>(
            new AddLoyaltyPoint(clientId, datetimeFirst, LoyaltyPointReceiveTypeKeys.FlightDomestic, 1000, "")
            {
                ReferenceVersion = loyaltyPoint.Version
            });
        Assert.NotNull(addPointResult);
        Assert.NotNull(addPointResult.AggregateId);

        loyaltyPoint = await _aggregateService.GetAggregateStateAsync<LoyaltyPoint>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(1000, loyaltyPoint!.Payload.CurrentPoint);

        // should throw not enough point error 
        await Assert.ThrowsAsync<SekibanLoyaltyPointNotEnoughException>(
            async () =>
            {
                await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, UseLoyaltyPoint>(
                    new UseLoyaltyPoint(clientId, datetimeFirst.AddSeconds(1), LoyaltyPointUsageTypeKeys.FlightUpgrade, 2000, "")
                    {
                        ReferenceVersion = addPointResult.Version
                    });
            });
        var (usePointResult, _) = await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, UseLoyaltyPoint>(
            new UseLoyaltyPoint(clientId, DateTime.Now, LoyaltyPointUsageTypeKeys.FlightUpgrade, 200, "")
            {
                ReferenceVersion = addPointResult.Version
            });
        Assert.NotNull(usePointResult);
        Assert.NotNull(usePointResult.AggregateId);

        loyaltyPoint = await _aggregateService.GetAggregateStateAsync<LoyaltyPoint>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(800, loyaltyPoint!.Payload.CurrentPoint);

        var p = await _multipleAggregateProjectionService
            .GetProjectionAsync<ClientLoyaltyPointMultipleProjection, ClientLoyaltyPointMultipleProjection.ContentsDefinition>();
        Assert.NotNull(p);
        Assert.Equal(3, p.Contents.Branches.Count);
        Assert.Single(p.Contents.Records);

        // delete client
        var (deleteClientResult, _) = await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, DeleteClient>(
            new DeleteClient(clientId) { ReferenceVersion = versionCN });
        Assert.NotNull(deleteClientResult);
        Assert.NotNull(deleteClientResult.AggregateId);
        // client deleted
        clientList = await _multipleAggregateProjectionService.GetAggregateList<Client>();
        Assert.Empty(clientList);
        // can find deleted client
        clientList = await _multipleAggregateProjectionService.GetAggregateList<Client>(QueryListType.DeletedOnly);
        Assert.Single(clientList);
        clientList = await _multipleAggregateProjectionService.GetAggregateList<Client>(QueryListType.ActiveAndDeleted);
        Assert.Single(clientList);

        // loyalty point should be created with event subscribe
        loyaltyPointList = await _multipleAggregateProjectionService.GetAggregateList<LoyaltyPoint>();
        Assert.Empty(loyaltyPointList);
        loyaltyPointList = await _multipleAggregateProjectionService.GetAggregateList<LoyaltyPoint>(QueryListType.DeletedOnly);
        Assert.Single(loyaltyPointList);

        // create recent activity
        var (createRecentActivityResult, _)
            = await _aggregateCommandExecutor.ExecCreateCommandAsync<RecentActivity, CreateRecentActivity>(
                new CreateRecentActivity());

        var recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);
        var version = createRecentActivityResult.Version;
        var count = 160;
        foreach (var i in Enumerable.Range(0, count))
        {
            var (recentActivityAddedResult, _)
                = await _aggregateCommandExecutor.ExecChangeCommandAsync<RecentActivity, AddRecentActivity>(
                    new AddRecentActivity(createRecentActivityResult.AggregateId!.Value, $"Message - {i + 1}") { ReferenceVersion = version });
            version = recentActivityAddedResult.Version;
        }
        recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);
        Assert.Equal(count + 1, version);

        // only publish event run

        var recentActivityId = createRecentActivityResult.AggregateId!.Value;

        await _aggregateCommandExecutor.ExecChangeCommandAsync<RecentActivity, OnlyPublishingAddRecentActivity>(
            new OnlyPublishingAddRecentActivity(recentActivityId, "only publish event"));

        // get single aggregate and applied event
        var recentActivityDto = await _aggregateService.GetAggregateStateAsync<RecentActivity>(recentActivityId);
        Assert.Equal("only publish event", recentActivityDto?.Payload.LatestActivities.First().Activity);

        p = await _multipleAggregateProjectionService
            .GetProjectionAsync<ClientLoyaltyPointMultipleProjection, ClientLoyaltyPointMultipleProjection.ContentsDefinition>();
        Assert.NotNull(p);
        Assert.Equal(3, p.Contents.Branches.Count);
        Assert.Empty(p.Contents.Records);
        var snapshotManager
            = await _aggregateService.GetAggregateStateFromInitialAsync<SnapshotManager>(
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

        branchList = await _multipleAggregateProjectionService.GetAggregateList<Branch>();
        Assert.Equal(3, branchList.Count);
    }
    [Fact(DisplayName = "CosmosDbストーリーテスト用に削除のみを行う 。")]
    public async Task DeleteonlyAsync()
    {
        // 先に全データを削除する
        await _cosmosDbFactory.DeleteAllFromAggregateEventContainer(AggregateContainerGroup.Default);
        await _cosmosDbFactory.DeleteAllFromAggregateEventContainer(AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.AggregateCommand, AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.AggregateCommand);
    }
    [Fact(DisplayName = "CosmosDb ストーリーテスト 。並列でたくさん動かしたらどうなるか。 INoValidateCommand がRecentActivityに適応されているので、問題ないはず")]
    public async Task AsynchronousExecutionTestAsync()
    {
        // 先に全データを削除する
        await _cosmosDbFactory.DeleteAllFromAggregateEventContainer(AggregateContainerGroup.Default);
        await _cosmosDbFactory.DeleteAllFromAggregateEventContainer(AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.AggregateCommand, AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.AggregateCommand);

        // create recent activity
        var (createRecentActivityResult, _)
            = await _aggregateCommandExecutor.ExecCreateCommandAsync<RecentActivity, CreateRecentActivity>(
                new CreateRecentActivity());
        var recentActivityId = createRecentActivityResult.AggregateId;

        var recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);
        var version = createRecentActivityResult.Version;
        var tasks = new List<Task>();
        var count = 80;
        foreach (var i in Enumerable.Range(0, count))
        {
            tasks.Add(
                Task.Run(
                    async () =>
                    {
                        var (recentActivityAddedResult, _)
                            = await _aggregateCommandExecutor.ExecChangeCommandAsync<RecentActivity, AddRecentActivity>(
                                new AddRecentActivity(createRecentActivityResult.AggregateId!.Value, $"Message - {i + 1}")
                                {
                                    ReferenceVersion = version
                                });
                        version = recentActivityAddedResult.Version;
                    }));
        }
        await Task.WhenAll(tasks);
        recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);
        // this works
        var aggregateRecentActivity
            = await _aggregateService.GetAggregateStateFromInitialAsync<RecentActivity>(
                createRecentActivityResult.AggregateId!.Value);
        var aggregateRecentActivity2
            = await _aggregateService.GetAggregateStateAsync<RecentActivity>(createRecentActivityResult.AggregateId!.Value);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + 1, aggregateRecentActivity!.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);

        var snapshotManager
            = await _aggregateService.GetAggregateStateFromInitialAsync<SnapshotManager>(
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
            typeof(RecentActivity));

        await CheckSnapshots<RecentActivity>(snapshots, createRecentActivityResult.AggregateId!.Value);

        _inMemoryDocumentStore.ResetInMemoryStore();
        _hybridStoreManager.ClearHybridPartitions();
        await ContinuousExecutionTestAsync();
    }

    private async Task CheckSnapshots<TAggregatePayload>(List<SnapshotDocument> snapshots, Guid aggregateId)
        where TAggregatePayload : IAggregatePayload, new()
    {
        foreach (var dto in snapshots.Select(snapshot => snapshot.ToDto<AggregateState<TAggregatePayload>>()))
        {
            if (dto is null) { throw new SekibanInvalidArgumentException(); }
            var fromInitial = await _aggregateService.GetAggregateStateFromInitialAsync<TAggregatePayload>(aggregateId, dto.Version);
            if (fromInitial is null) { throw new SekibanInvalidArgumentException(); }
            Assert.Equal(fromInitial.Version, dto.Version);
            Assert.Equal(fromInitial.LastEventId, dto.LastEventId);
        }
    }
    [Fact(DisplayName = "インメモリストーリーテスト 。並列でたくさん動かしたらどうなるか。 Versionの重複が発生しないことを確認")]
    public async Task AsynchronousInMemoryExecutionTestAsync()
    {
        // create recent activity
        var (createRecentActivityResult, _)
            = await _aggregateCommandExecutor
                .ExecCreateCommandAsync<RecentInMemoryActivity, CreateRecentInMemoryActivity>(
                    new CreateRecentInMemoryActivity());

        var recentActivityList = await _multipleAggregateProjectionService.GetAggregateList< RecentInMemoryActivity>();
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
                        var (recentActivityAddedResult, _)
                            = await _aggregateCommandExecutor
                                .ExecChangeCommandAsync< RecentInMemoryActivity, AddRecentInMemoryActivity>(
                                    new AddRecentInMemoryActivity(createRecentActivityResult.AggregateId!.Value, $"Message - {i + 1}")
                                    {
                                        ReferenceVersion = version
                                    });
                        version = recentActivityAddedResult.Version;
                        _testOutputHelper.WriteLine($"{i} - {recentActivityAddedResult.Version.ToString()}");
                    }));
        }
        await Task.WhenAll(tasks);
        recentActivityList = await _multipleAggregateProjectionService.GetAggregateList< RecentInMemoryActivity>();
        Assert.Single(recentActivityList);

        var aggregateRecentActivity
            = await _aggregateService.GetAggregateStateFromInitialAsync< RecentInMemoryActivity>(
                createRecentActivityResult.AggregateId!.Value);
        var aggregateRecentActivity2
            = await _aggregateService.GetAggregateStateAsync< RecentInMemoryActivity>(
                createRecentActivityResult.AggregateId!.Value);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + 1, aggregateRecentActivity!.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);
    }

    public async Task ContinuousExecutionTestAsync()
    {
        var recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);
        var aggregateId = recentActivityList.First().AggregateId;

        var aggregate = await _aggregateService.GetAggregateAsync<RecentActivity>(aggregateId);
        Assert.NotNull(aggregate);

        var aggregateRecentActivity2 = await _aggregateService.GetAggregateStateAsync<RecentActivity>(aggregateId);
        //var aggregateRecentActivity =
        //    await _aggregateService
        //        .GetAggregateStateFromInitialAsync<RecentActivity>(
        //            aggregateId);
        //Assert.Single(recentActivityList);
        //Assert.NotNull(aggregateRecentActivity);
        //Assert.NotNull(aggregateRecentActivity2);
        //Assert.Equal(aggregateRecentActivity!.Version, aggregateRecentActivity2!.Version);

        var version = recentActivityList.First().Version;
        var tasks = new List<Task>();
        var count = 280;
        foreach (var i in Enumerable.Range(0, count))
        {
            tasks.Add(
                Task.Run(
                    async () =>
                    {
                        var (recentActivityAddedResult, _)
                            = await _aggregateCommandExecutor.ExecChangeCommandAsync<RecentActivity, AddRecentActivity>(
                                new AddRecentActivity(aggregateId, $"Message - {i + 1}") { ReferenceVersion = version });
                        version = recentActivityAddedResult.Version;
                    }));
        }
        await Task.WhenAll(tasks);
        recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);

        var snapshots = await _documentPersistentRepository.GetSnapshotsForAggregateAsync(aggregateId, typeof(RecentActivity));
        await CheckSnapshots<RecentActivity>(snapshots, aggregateId);

        // check aggregate result
        var aggregateRecentActivity
            = await _aggregateService.GetAggregateStateFromInitialAsync<RecentActivity>(aggregateId);
        aggregateRecentActivity2 = await _aggregateService.GetAggregateStateAsync<RecentActivity>(aggregateId);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + aggregate!.ToState().Version, aggregateRecentActivity!.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);

        var snapshotManager
            = await _aggregateService.GetAggregateStateFromInitialAsync<SnapshotManager>(
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
