using CosmosInfrastructure;
using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Branches.Commands;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Commands;
using CustomerDomainContext.Aggregates.Clients.Projections;
using CustomerDomainContext.Aggregates.LoyaltyPoints;
using CustomerDomainContext.Aggregates.LoyaltyPoints.Commands;
using CustomerDomainContext.Aggregates.LoyaltyPoints.Consts;
using CustomerDomainContext.Aggregates.RecentActivities;
using CustomerDomainContext.Aggregates.RecentActivities.Commands;
using CustomerDomainContext.Aggregates.RecentInMemoryActivities;
using CustomerDomainContext.Aggregates.RecentInMemoryActivities.Commands;
using CustomerDomainContext.Projections.ClientLoyaltyPointMultiples;
using CustomerDomainContext.Shared.Exceptions;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Documents;
using Sekiban.EventSourcing.Queries;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.Shared.Exceptions;
using Sekiban.EventSourcing.Snapshots;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers;
using Sekiban.EventSourcing.TestHelpers;
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
    public CustomerDbStoryBasic(TestFixture testFixture, ITestOutputHelper testOutputHelper) : base(testFixture)
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
        var branchList = await _multipleAggregateProjectionService.GetAggregateList<Branch, BranchContents>();
        Assert.Empty(branchList);
        var branchResult = await _aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchContents, CreateBranch>(new CreateBranch("Japan"));
        var branchId = branchResult!.Command!.AggregateId;
        Assert.NotNull(branchResult);
        Assert.NotNull(branchResult.AggregateDto);
        branchList = await _multipleAggregateProjectionService.GetAggregateList<Branch, BranchContents>();
        Assert.Single(branchList);
        var branchFromList = branchList.First(m => m.AggregateId == branchId);
        Assert.NotNull(branchFromList);

        var branchResult2 = await _aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchContents, CreateBranch>(new CreateBranch("USA"));
        branchList = await _multipleAggregateProjectionService.GetAggregateList<Branch, BranchContents>();
        Assert.Equal(2, branchList.Count);
        var branchListFromMultiple = await _multipleAggregateProjectionService.GetAggregateList<Branch, BranchContents>();
        Assert.Equal(2, branchListFromMultiple.Count);

        // loyalty point should be []  
        var loyaltyPointList = await _multipleAggregateProjectionService.GetAggregateList<LoyaltyPoint, LoyaltyPointContents>();
        Assert.Empty(loyaltyPointList);

        var clientNameList = await _multipleAggregateProjectionService
            .GetSingleAggregateProjectionList<Client, ClientNameHistoryProjection, ClientNameHistoryProjection.ContentsDefinition>();
        Assert.Empty(clientNameList);

        // create client
        var clientList = await _multipleAggregateProjectionService.GetAggregateList<Client, ClientContents>();
        Assert.Empty(clientList);
        var originalName = "Tanaka Taro";
        var createClientResult = await _aggregateCommandExecutor.ExecCreateCommandAsync<Client, ClientContents, CreateClient>(
            new CreateClient(branchId, originalName, "tanaka@example.com"));
        var clientId = createClientResult.Command!.AggregateId;
        Assert.NotNull(createClientResult);
        Assert.NotNull(createClientResult.AggregateDto);
        clientList = await _multipleAggregateProjectionService.GetAggregateList<Client, ClientContents>();
        Assert.Single(clientList);

        // singleAggregateProjection
        clientNameList = await _multipleAggregateProjectionService
            .GetSingleAggregateProjectionList<Client, ClientNameHistoryProjection, ClientNameHistoryProjection.ContentsDefinition>();
        Assert.Single(clientNameList);
        var tanakaProjection = clientNameList.First(m => m.AggregateId == clientId);
        Assert.Single(tanakaProjection.Contents.ClientNames);
        Assert.Equal(originalName, tanakaProjection.Contents.ClientNames.First().Name);

        var clientNameListFromMultiple
            = await _multipleAggregateProjectionService.GetSingleAggregateProjectionList<Client, ClientNameHistoryProjection, ClientNameHistoryProjection.ContentsDefinition>();
        Assert.Single(clientNameListFromMultiple);
        Assert.Equal(clientNameList.First().AggregateId, clientNameListFromMultiple.First().AggregateId);

        var branchResult3
            = await _aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchContents, CreateBranch>(new CreateBranch("California"));
        branchList = await _multipleAggregateProjectionService.GetAggregateList<Branch, BranchContents>();

        var secondName = "田中 太郎";
        // should throw version error 
        await Assert.ThrowsAsync<SekibanAggregateCommandInconsistentVersionException>(
            async () =>
            {
                await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ClientContents, ChangeClientName>(
                    new ChangeClientName(clientId, secondName));
            });
        // change name
        var changeNameResult = await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ClientContents, ChangeClientName>(
            new ChangeClientName(clientId, secondName) { ReferenceVersion = createClientResult.AggregateDto!.Version });

        // change name projection
        clientNameList = await _multipleAggregateProjectionService.GetSingleAggregateProjectionList<Client, ClientNameHistoryProjection, ClientNameHistoryProjection.ContentsDefinition>();
        Assert.Single(clientNameList);
        tanakaProjection = clientNameList.First(m => m.AggregateId == clientId);
        Assert.Equal(2, tanakaProjection.Contents.ClientNames.Count);
        Assert.Equal(originalName, tanakaProjection.Contents.ClientNames.First().Name);
        Assert.Equal(secondName, tanakaProjection.Contents.ClientNames.ToList()[1].Name);

        // test change name multiple time to create projection 
        var versionCN = changeNameResult!.AggregateDto!.Version;
        var countChangeName = 160;
        foreach (var i in Enumerable.Range(0, countChangeName))
        {
            var changeNameResult2 = await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ClientContents, ChangeClientName>(
                new ChangeClientName(clientId, $"newname - {i + 1}") { ReferenceVersion = versionCN });
            versionCN = changeNameResult2.AggregateDto!.Version;
        }

        // get change name dto
        var changeNameProjection = await _aggregateService.GetProjectionAsync<Client, ClientNameHistoryProjection, ClientNameHistoryProjection.ContentsDefinition>(clientId);
        Assert.NotNull(changeNameProjection);

        // loyalty point should be created with event subscribe
        loyaltyPointList = await _multipleAggregateProjectionService.GetAggregateList<LoyaltyPoint, LoyaltyPointContents>();
        Assert.Single(clientList);
        // first point = 0
        var loyaltyPoint = await _aggregateService.GetAggregateDtoAsync<LoyaltyPoint, LoyaltyPointContents>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(0, loyaltyPoint!.Contents.CurrentPoint);

        var datetimeFirst = DateTime.Now;
        var addPointResult = await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointContents, AddLoyaltyPoint>(
            new AddLoyaltyPoint(clientId, datetimeFirst, LoyaltyPointReceiveTypeKeys.FlightDomestic, 1000, "")
            {
                ReferenceVersion = loyaltyPoint.Version
            });
        Assert.NotNull(addPointResult);
        Assert.NotNull(addPointResult.AggregateDto);

        loyaltyPoint = await _aggregateService.GetAggregateDtoAsync<LoyaltyPoint, LoyaltyPointContents>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(1000, loyaltyPoint!.Contents.CurrentPoint);

        // should throw not enough point error 
        await Assert.ThrowsAsync<SekibanLoyaltyPointNotEnoughException>(
            async () =>
            {
                await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointContents, UseLoyaltyPoint>(
                    new UseLoyaltyPoint(clientId, datetimeFirst.AddSeconds(1), LoyaltyPointUsageTypeKeys.FlightUpgrade, 2000, "")
                    {
                        ReferenceVersion = addPointResult!.AggregateDto!.Version
                    });
            });
        var usePointResult = await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointContents, UseLoyaltyPoint>(
            new UseLoyaltyPoint(clientId, DateTime.Now, LoyaltyPointUsageTypeKeys.FlightUpgrade, 200, "")
            {
                ReferenceVersion = addPointResult!.AggregateDto!.Version
            });
        Assert.NotNull(usePointResult);
        Assert.NotNull(usePointResult.AggregateDto);

        loyaltyPoint = await _aggregateService.GetAggregateDtoAsync<LoyaltyPoint, LoyaltyPointContents>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(800, loyaltyPoint!.Contents.CurrentPoint);

        var p = await _multipleAggregateProjectionService
            .GetProjectionAsync<ClientLoyaltyPointMultipleProjection, ClientLoyaltyPointMultipleProjection.ContentsDefinition>();
        Assert.NotNull(p);
        Assert.Equal(3, p.Contents.Branches.Count);
        Assert.Single(p.Contents.Records);

        // delete client
        var deleteClientResult = await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ClientContents, DeleteClient>(
            new DeleteClient(clientId) { ReferenceVersion = versionCN });
        Assert.NotNull(deleteClientResult);
        Assert.NotNull(deleteClientResult.AggregateDto);
        // client deleted
        clientList = await _multipleAggregateProjectionService.GetAggregateList<Client, ClientContents>();
        Assert.Empty(clientList);
        // can find deleted client
        clientList = await _multipleAggregateProjectionService.GetAggregateList<Client, ClientContents>(QueryListType.DeletedOnly);
        Assert.Single(clientList);
        clientList = await _multipleAggregateProjectionService.GetAggregateList<Client, ClientContents>(QueryListType.ActiveAndDeleted);
        Assert.Single(clientList);

        // loyalty point should be created with event subscribe
        loyaltyPointList = await _multipleAggregateProjectionService.GetAggregateList<LoyaltyPoint, LoyaltyPointContents>();
        Assert.Empty(loyaltyPointList);
        loyaltyPointList = await _multipleAggregateProjectionService.GetAggregateList<LoyaltyPoint, LoyaltyPointContents>(QueryListType.DeletedOnly);
        Assert.Single(loyaltyPointList);

        // create recent activity
        var createRecentActivityResult
            = await _aggregateCommandExecutor.ExecCreateCommandAsync<RecentActivity, RecentActivityContents, CreateRecentActivity>(
                new CreateRecentActivity());

        var recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity, RecentActivityContents>();
        Assert.Single(recentActivityList);
        var version = createRecentActivityResult.AggregateDto!.Version;
        var count = 160;
        foreach (var i in Enumerable.Range(0, count))
        {
            var recentActivityAddedResult
                = await _aggregateCommandExecutor.ExecChangeCommandAsync<RecentActivity, RecentActivityContents, AddRecentActivity>(
                    new AddRecentActivity(
                        createRecentActivityResult!.AggregateDto!.AggregateId,
                        $"Message - {i + 1}") { ReferenceVersion = version });
            version = recentActivityAddedResult.AggregateDto!.Version;
        }
        recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity, RecentActivityContents>();
        Assert.Single(recentActivityList);
        Assert.Equal(count + 1, version);

        p = await _multipleAggregateProjectionService
            .GetProjectionAsync<ClientLoyaltyPointMultipleProjection, ClientLoyaltyPointMultipleProjection.ContentsDefinition>();
        Assert.NotNull(p);
        Assert.Equal(3, p.Contents.Branches.Count);
        Assert.Empty(p.Contents.Records);
        var snapshotManager
            = await _aggregateService.GetAggregateFromInitialDefaultAggregateDtoAsync<SnapshotManager, SnapshotManagerContents>(
                SnapshotManager.SharedId);
        _testOutputHelper.WriteLine("-requests-");
        foreach (var key in snapshotManager!.Contents.Requests)
        {
            _testOutputHelper.WriteLine(key);
        }
        _testOutputHelper.WriteLine("-request takens-");
        foreach (var key in snapshotManager!.Contents.RequestTakens)
        {
            _testOutputHelper.WriteLine(key);
        }

        branchList = await _multipleAggregateProjectionService.GetAggregateList<Branch, BranchContents>();
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
        var createRecentActivityResult
            = await _aggregateCommandExecutor.ExecCreateCommandAsync<RecentActivity, RecentActivityContents, CreateRecentActivity>(
                new CreateRecentActivity());
        var recentActivityId = createRecentActivityResult.Command!.AggregateId;

        var recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity, RecentActivityContents>();
        Assert.Single(recentActivityList);
        var version = createRecentActivityResult.AggregateDto!.Version;
        var tasks = new List<Task>();
        var count = 80;
        foreach (var i in Enumerable.Range(0, count))
        {
            tasks.Add(
                Task.Run(
                    async () =>
                    {
                        var recentActivityAddedResult
                            = await _aggregateCommandExecutor.ExecChangeCommandAsync<RecentActivity, RecentActivityContents, AddRecentActivity>(
                                new AddRecentActivity(createRecentActivityResult!.AggregateDto!.AggregateId, $"Message - {i + 1}")
                                {
                                    ReferenceVersion = version
                                });
                        version = recentActivityAddedResult.AggregateDto!.Version;
                    }));
        }
        await Task.WhenAll(tasks);
        recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity, RecentActivityContents>();
        Assert.Single(recentActivityList);
        // this works
        var aggregateRecentActivity
            = await _aggregateService.GetAggregateFromInitialDefaultAggregateDtoAsync<RecentActivity, RecentActivityContents>(
                createRecentActivityResult.AggregateDto.AggregateId);
        var aggregateRecentActivity2
            = await _aggregateService.GetAggregateDtoAsync<RecentActivity, RecentActivityContents>(
                createRecentActivityResult.AggregateDto.AggregateId);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + 1, aggregateRecentActivity!.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);

        var snapshotManager
            = await _aggregateService.GetAggregateFromInitialDefaultAggregateDtoAsync<SnapshotManager, SnapshotManagerContents>(
                SnapshotManager.SharedId);
        _testOutputHelper.WriteLine("-requests-");
        foreach (var key in snapshotManager!.Contents.Requests)
        {
            _testOutputHelper.WriteLine(key);
        }
        _testOutputHelper.WriteLine("-request takens-");
        foreach (var key in snapshotManager!.Contents.RequestTakens)
        {
            _testOutputHelper.WriteLine(key);
        }

        var snapshots = await _documentPersistentRepository.GetSnapshotsForAggregateAsync(
            createRecentActivityResult.AggregateDto.AggregateId,
            typeof(RecentActivity));

        await CheckSnapshots<RecentActivity, RecentActivityContents>(snapshots, createRecentActivityResult.AggregateDto.AggregateId);

        _inMemoryDocumentStore.ResetInMemoryStore();
        _hybridStoreManager.ClearHybridPartitions();
        await ContinuousExecutionTestAsync();
    }

    private async Task CheckSnapshots<T, TContents>(List<SnapshotDocument> snapshots, Guid aggregateId)
        where T : TransferableAggregateBase<TContents>, new() where TContents : IAggregateContents, new()
    {
        foreach (var dto in snapshots.Select(snapshot => snapshot.ToDto<AggregateDto<TContents>>()))
        {
            if (dto is null) { throw new SekibanInvalidArgumentException(); }
            var fromInitial = await _aggregateService.GetAggregateFromInitialDefaultAggregateDtoAsync<T, TContents>(aggregateId, dto.Version);
            if (fromInitial is null) { throw new SekibanInvalidArgumentException(); }
            Assert.Equal(fromInitial.Version, dto.Version);
            Assert.Equal(fromInitial.LastEventId, dto.LastEventId);
        }
    }
    [Fact(DisplayName = "インメモリストーリーテスト 。並列でたくさん動かしたらどうなるか。 Versionの重複が発生しないことを確認")]
    public async Task AsynchronousInMemoryExecutionTestAsync()
    {
        // create recent activity
        var createRecentActivityResult
            = await _aggregateCommandExecutor
                .ExecCreateCommandAsync<RecentInMemoryActivity, RecentInMemoryActivityContents, CreateRecentInMemoryActivity>(
                    new CreateRecentInMemoryActivity());

        var recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentInMemoryActivity, RecentInMemoryActivityContents>();
        Assert.Single(recentActivityList);
        var version = createRecentActivityResult.AggregateDto!.Version;
        var tasks = new List<Task>();
        var count = 140;
        foreach (var i in Enumerable.Range(0, count))
        {
            tasks.Add(
                Task.Run(
                    async () =>
                    {
                        var recentActivityAddedResult
                            = await _aggregateCommandExecutor
                                .ExecChangeCommandAsync<RecentInMemoryActivity, RecentInMemoryActivityContents, AddRecentInMemoryActivity>(
                                    new AddRecentInMemoryActivity(createRecentActivityResult!.AggregateDto!.AggregateId, $"Message - {i + 1}")
                                    {
                                        ReferenceVersion = version
                                    });
                        version = recentActivityAddedResult.AggregateDto!.Version;
                        _testOutputHelper.WriteLine($"{i} - {recentActivityAddedResult.AggregateDto.Version.ToString()}");
                    }));
        }
        await Task.WhenAll(tasks);
        recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentInMemoryActivity, RecentInMemoryActivityContents>();
        Assert.Single(recentActivityList);

        var aggregateRecentActivity
            = await _aggregateService.GetAggregateFromInitialDefaultAggregateDtoAsync<RecentInMemoryActivity, RecentInMemoryActivityContents>(
                createRecentActivityResult.AggregateDto.AggregateId);
        var aggregateRecentActivity2
            = await _aggregateService.GetAggregateDtoAsync<RecentInMemoryActivity, RecentInMemoryActivityContents>(
                createRecentActivityResult.AggregateDto.AggregateId);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + 1, aggregateRecentActivity!.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);
    }

    public async Task ContinuousExecutionTestAsync()
    {
        var recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity, RecentActivityContents>();
        Assert.Single(recentActivityList);
        var aggregateId = recentActivityList.First().AggregateId;

        var aggregate = await _aggregateService.GetAggregateAsync<RecentActivity, RecentActivityContents>(aggregateId);
        Assert.NotNull(aggregate);

        var aggregateRecentActivity2 = await _aggregateService.GetAggregateDtoAsync<RecentActivity, RecentActivityContents>(aggregateId);
        //var aggregateRecentActivity =
        //    await _aggregateService
        //        .GetAggregateFromInitialDefaultAggregateDtoAsync<RecentActivity, RecentActivityContents>(
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
                        var recentActivityAddedResult
                            = await _aggregateCommandExecutor.ExecChangeCommandAsync<RecentActivity, RecentActivityContents, AddRecentActivity>(
                                new AddRecentActivity(aggregateId, $"Message - {i + 1}") { ReferenceVersion = version });
                        version = recentActivityAddedResult.AggregateDto!.Version;
                    }));
        }
        await Task.WhenAll(tasks);
        recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity, RecentActivityContents>();
        Assert.Single(recentActivityList);

        var snapshots = await _documentPersistentRepository.GetSnapshotsForAggregateAsync(aggregateId, typeof(RecentActivity));
        await CheckSnapshots<RecentActivity, RecentActivityContents>(snapshots, aggregateId);

        // check aggregate result
        var aggregateRecentActivity
            = await _aggregateService.GetAggregateFromInitialDefaultAggregateDtoAsync<RecentActivity, RecentActivityContents>(aggregateId);
        aggregateRecentActivity2 = await _aggregateService.GetAggregateDtoAsync<RecentActivity, RecentActivityContents>(aggregateId);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + aggregate!.ToDto().Version, aggregateRecentActivity!.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);

        var snapshotManager
            = await _aggregateService.GetAggregateFromInitialDefaultAggregateDtoAsync<SnapshotManager, SnapshotManagerContents>(
                SnapshotManager.SharedId);
        _testOutputHelper.WriteLine("-requests-");
        foreach (var key in snapshotManager!.Contents.Requests)
        {
            _testOutputHelper.WriteLine(key);
        }
        _testOutputHelper.WriteLine("-request takens-");
        foreach (var key in snapshotManager!.Contents.RequestTakens)
        {
            _testOutputHelper.WriteLine(key);
        }
    }
}
