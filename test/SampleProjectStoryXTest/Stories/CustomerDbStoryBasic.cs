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
using CustomerDomainContext.Shared.Exceptions;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Documents;
using Sekiban.EventSourcing.Queries;
using Sekiban.EventSourcing.Shared.Exceptions;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest.Stories;

public class CustomerDbStoryBasic : TestBase
{
    private readonly AggregateCommandExecutor _aggregateCommandExecutor;
    private readonly SingleAggregateService _aggregateService;
    private readonly CosmosDbFactory _cosmosDbFactory;
    private readonly IDocumentPersistentRepository _documentPersistentRepository;
    private readonly ITestOutputHelper _testOutputHelper;
    public CustomerDbStoryBasic(TestFixture testFixture, ITestOutputHelper testOutputHelper) : base(
        testFixture)
    {
        _testOutputHelper = testOutputHelper;
        _cosmosDbFactory = GetService<CosmosDbFactory>();
        _aggregateCommandExecutor = GetService<AggregateCommandExecutor>();
        _aggregateService = GetService<SingleAggregateService>();
        _documentPersistentRepository = GetService<IDocumentPersistentRepository>();
        // create recent activity
        _aggregateCommandExecutor
            .ExecCreateCommandAsync<SnapshotManager, SnapshotManagerDto,
                CreateSnapshotManager>(
                new CreateSnapshotManager(SnapshotManager.SharedId))
            .Wait();
    }
    [Fact(DisplayName = "CosmosDb ストーリーテスト 集約の機能のテストではなく、CosmosDbと連携して正しく動くかをテストしています。")]
    public async Task CosmosDbStory()
    {
        // 先に全データを削除する
        await _cosmosDbFactory.DeleteAllFromAggregateEventContainer(
            AggregateContainerGroup.Default);
        await _cosmosDbFactory.DeleteAllFromAggregateEventContainer(
            AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(
            DocumentType.AggregateCommand,
            AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(
            DocumentType.AggregateCommand);

        // create list branch
        var branchList = (await _aggregateService.DtoListAsync<Branch, BranchDto>()).ToList();
        Assert.Empty(branchList);
        var branchResult =
            await _aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchDto, CreateBranch>(
                new CreateBranch("Japan"));
        Assert.NotNull(branchResult);
        Assert.NotNull(branchResult.AggregateDto);
        var branchId = branchResult.AggregateDto!.AggregateId;
        branchList = (await _aggregateService.DtoListAsync<Branch, BranchDto>()).ToList();
        Assert.Single(branchList);
        var branchFromList =
            branchList.First(m => m.AggregateId == branchId);
        Assert.NotNull(branchFromList);

        // loyalty point should be []  
        var loyaltyPointList =
            (await _aggregateService.DtoListAsync<LoyaltyPoint, LoyaltyPointDto>()).ToList();
        Assert.Empty(loyaltyPointList);

        var clientNameList =
            (await _aggregateService.DtoListAsync<ClientNameHistoryProjection>()).ToList();
        Assert.Empty(clientNameList);

        // create client
        var clientList = (await _aggregateService.DtoListAsync<Client, ClientDto>()).ToList();
        Assert.Empty(clientList);
        var originalName = "Tanaka Taro";
        var createClientResult =
            await _aggregateCommandExecutor.ExecCreateCommandAsync<Client, ClientDto, CreateClient>(
                new CreateClient(branchId, originalName, "tanaka@example.com"));
        Assert.NotNull(createClientResult);
        Assert.NotNull(createClientResult.AggregateDto);
        var clientId = createClientResult.AggregateDto!.AggregateId;
        clientList = (await _aggregateService.DtoListAsync<Client, ClientDto>()).ToList();
        Assert.Single(clientList);

        // singleAggregateProjection
        clientNameList =
            (await _aggregateService.DtoListAsync<ClientNameHistoryProjection>()).ToList();
        Assert.Single(clientNameList);
        var tanakaProjection = clientNameList.First(m => m.AggregateId == clientId);
        Assert.Single(tanakaProjection.ClientNames);
        Assert.Equal(originalName, tanakaProjection.ClientNames.First().Name);
        var secondName = "田中 太郎";

        // should throw version error 
        await Assert.ThrowsAsync<JJAggregateCommandInconsistentVersionException>(
            async () =>
            {
                await _aggregateCommandExecutor
                    .ExecChangeCommandAsync<Client, ClientDto, ChangeClientName>(
                        new ChangeClientName(clientId, secondName));
            }
        );
        // change name
        var changeNameResult =
            await _aggregateCommandExecutor
                .ExecChangeCommandAsync<Client, ClientDto, ChangeClientName>(
                    new ChangeClientName(clientId, secondName)
                        { ReferenceVersion = createClientResult.AggregateDto!.Version });
        // change name projection
        clientNameList =
            (await _aggregateService.DtoListAsync<ClientNameHistoryProjection>()).ToList();
        Assert.Single(clientNameList);
        tanakaProjection = clientNameList.First(m => m.AggregateId == clientId);
        Assert.Equal(2, tanakaProjection.ClientNames.Count);
        Assert.Equal(originalName, tanakaProjection.ClientNames.First().Name);
        Assert.Equal(secondName, tanakaProjection.ClientNames[1].Name);

        // loyalty point should be created with event subscribe
        loyaltyPointList = (await _aggregateService.DtoListAsync<LoyaltyPoint, LoyaltyPointDto>())
            .ToList();
        Assert.Single(clientList);
        // first point = 0
        var loyaltyPoint =
            await _aggregateService.GetAggregateDtoAsync<LoyaltyPoint, LoyaltyPointDto>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(0, loyaltyPoint!.CurrentPoint);

        var datetimeFirst = DateTime.Now;
        var addPointResult =
            await _aggregateCommandExecutor
                .ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointDto, AddLoyaltyPoint>(
                    new AddLoyaltyPoint(
                        clientId,
                        datetimeFirst,
                        LoyaltyPointReceiveTypeKeys.FlightDomestic,
                        1000,
                        "") { ReferenceVersion = loyaltyPoint.Version });
        Assert.NotNull(addPointResult);
        Assert.NotNull(addPointResult.AggregateDto);

        loyaltyPoint =
            await _aggregateService.GetAggregateDtoAsync<LoyaltyPoint, LoyaltyPointDto>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(1000, loyaltyPoint!.CurrentPoint);

        // should throw not enough point error 
        await Assert.ThrowsAsync<JJLoyaltyPointNotEnoughException>(
            async () =>
            {
                await _aggregateCommandExecutor
                    .ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointDto, UseLoyaltyPoint>(
                        new UseLoyaltyPoint(
                            clientId,
                            datetimeFirst.AddSeconds(1),
                            LoyaltyPointUsageTypeKeys.FlightUpgrade,
                            2000,
                            "") { ReferenceVersion = addPointResult!.AggregateDto!.Version });
            }
        );
        var usePointResult = await _aggregateCommandExecutor
            .ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointDto, UseLoyaltyPoint>(
                new UseLoyaltyPoint(
                    clientId,
                    DateTime.Now,
                    LoyaltyPointUsageTypeKeys.FlightUpgrade,
                    200,
                    "") { ReferenceVersion = addPointResult!.AggregateDto!.Version });
        Assert.NotNull(usePointResult);
        Assert.NotNull(usePointResult.AggregateDto);

        loyaltyPoint =
            await _aggregateService.GetAggregateDtoAsync<LoyaltyPoint, LoyaltyPointDto>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(800, loyaltyPoint!.CurrentPoint);
        // delete client
        var deleteClientResult = await _aggregateCommandExecutor
            .ExecChangeCommandAsync<Client, ClientDto, DeleteClient>(
                new DeleteClient(clientId)
                    { ReferenceVersion = changeNameResult!.AggregateDto!.Version });
        Assert.NotNull(deleteClientResult);
        Assert.NotNull(deleteClientResult.AggregateDto);
        // client deleted
        clientList = (await _aggregateService.DtoListAsync<Client, ClientDto>()).ToList();
        Assert.Empty(clientList);
        // can find deleted client
        clientList =
            (await _aggregateService.DtoListAsync<Client, ClientDto>(QueryListType.DeletedOnly))
            .ToList();
        Assert.Single(clientList);
        clientList =
            (await _aggregateService.DtoListAsync<Client, ClientDto>(
                QueryListType.ActiveAndDeleted)).ToList();
        Assert.Single(clientList);

        // loyalty point should be created with event subscribe
        loyaltyPointList = (await _aggregateService.DtoListAsync<LoyaltyPoint, LoyaltyPointDto>())
            .ToList();
        Assert.Empty(loyaltyPointList);
        loyaltyPointList =
            (await _aggregateService.DtoListAsync<LoyaltyPoint, LoyaltyPointDto>(
                QueryListType.DeletedOnly)).ToList();
        Assert.Single(loyaltyPointList);

        // create recent activity
        var createRecentActivityResult =
            await _aggregateCommandExecutor
                .ExecCreateCommandAsync<RecentActivity, RecentActivityDto, CreateRecentActivity>(
                    new CreateRecentActivity(Guid.NewGuid()));

        var recentActivityList =
            (await _aggregateService.DtoListAsync<RecentActivity, RecentActivityDto>()).ToList();
        Assert.Single(recentActivityList);
        var version = createRecentActivityResult.AggregateDto!.Version;
        var count = 180;
        foreach (var i in Enumerable.Range(0, count))
        {
            var recentActivityAddedResult = await
                _aggregateCommandExecutor
                    .ExecChangeCommandAsync<RecentActivity, RecentActivityDto, AddRecentActivity>(
                        new AddRecentActivity(
                            createRecentActivityResult!.AggregateDto!.AggregateId,
                            $"Message - {i + 1}") { ReferenceVersion = version });
            version = recentActivityAddedResult.AggregateDto!.Version;
        }
        recentActivityList =
            (await _aggregateService.DtoListAsync<RecentActivity, RecentActivityDto>()).ToList();
        Assert.Single(recentActivityList);
        Assert.Equal(count + 1, version);

        var snapshotManager =
            await _aggregateService
                .GetAggregateFromInitialDefaultAggregateDtoAsync<SnapshotManager,
                    SnapshotManagerDto>(SnapshotManager.SharedId);
        _testOutputHelper.WriteLine("-requests-");
        foreach (var key in snapshotManager!.Requests)
        {
            _testOutputHelper.WriteLine(key);
        }
        _testOutputHelper.WriteLine("-request takens-");
        foreach (var key in snapshotManager!.RequestTakens)
        {
            _testOutputHelper.WriteLine(key);
        }
    }
    [Fact(
        DisplayName =
            "CosmosDbストーリーテスト用に削除のみを行う 。")]
    public async Task DeleteonlyAsync()
    {
        // 先に全データを削除する
        await _cosmosDbFactory.DeleteAllFromAggregateEventContainer(
            AggregateContainerGroup.Default);
        await _cosmosDbFactory.DeleteAllFromAggregateEventContainer(
            AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(
            DocumentType.AggregateCommand,
            AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(
            DocumentType.AggregateCommand);
    }
    [Fact(
        DisplayName =
            "CosmosDb ストーリーテスト 。並列でたくさん動かしたらどうなるか。 INoValidateCommand がRecentActivityに適応されているので、問題ないはず")]
    public async Task AsynchronousExecutionTestAsync()
    {
        // 先に全データを削除する
        await _cosmosDbFactory.DeleteAllFromAggregateEventContainer(
            AggregateContainerGroup.Default);
        await _cosmosDbFactory.DeleteAllFromAggregateEventContainer(
            AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(
            DocumentType.AggregateCommand,
            AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(
            DocumentType.AggregateCommand);

        // create recent activity
        var createRecentActivityResult =
            await _aggregateCommandExecutor
                .ExecCreateCommandAsync<RecentActivity, RecentActivityDto, CreateRecentActivity>(
                    new CreateRecentActivity(Guid.NewGuid()));

        var recentActivityList =
            (await _aggregateService.DtoListAsync<RecentActivity, RecentActivityDto>()).ToList();
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
                        var recentActivityAddedResult = await
                            _aggregateCommandExecutor
                                .ExecChangeCommandAsync<RecentActivity, RecentActivityDto,
                                    AddRecentActivity>(
                                    new AddRecentActivity(
                                        createRecentActivityResult!.AggregateDto!.AggregateId,
                                        $"Message - {i + 1}") { ReferenceVersion = version });
                        version = recentActivityAddedResult.AggregateDto!.Version;
                    }));
        }
        await Task.WhenAll(tasks);
        recentActivityList =
            (await _aggregateService.DtoListAsync<RecentActivity, RecentActivityDto>()).ToList();
        Assert.Single(recentActivityList);
        // this works
        var aggregateRecentActivity =
            await _aggregateService
                .GetAggregateFromInitialDefaultAggregateDtoAsync<RecentActivity, RecentActivityDto>(
                    createRecentActivityResult.AggregateDto.AggregateId);
        var aggregateRecentActivity2 =
            await _aggregateService.GetAggregateDtoAsync<RecentActivity, RecentActivityDto>(
                createRecentActivityResult.AggregateDto.AggregateId);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + 1, aggregateRecentActivity!.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);

        var snapshotManager =
            await _aggregateService
                .GetAggregateFromInitialDefaultAggregateDtoAsync<SnapshotManager,
                    SnapshotManagerDto>(SnapshotManager.SharedId);
        _testOutputHelper.WriteLine("-requests-");
        foreach (var key in snapshotManager!.Requests)
        {
            _testOutputHelper.WriteLine(key);
        }
        _testOutputHelper.WriteLine("-request takens-");
        foreach (var key in snapshotManager!.RequestTakens)
        {
            _testOutputHelper.WriteLine(key);
        }

        var snapshots = await _documentPersistentRepository.GetSnapshotsForAggregateAsync(
            createRecentActivityResult.AggregateDto.AggregateId,
            typeof(RecentActivity));

        foreach (var snapshot in snapshots)
        {
            var dto = snapshot.ToDto<RecentActivityDto>();
            var fromInitial =
                await _aggregateService
                    .GetAggregateFromInitialDefaultAggregateDtoAsync<RecentActivity,
                        RecentActivityDto>(
                        createRecentActivityResult.AggregateDto.AggregateId,
                        dto.Version);
            Assert.Equal(fromInitial.Version, dto.Version);
            Assert.Equal(fromInitial.LastEventId, dto.LastEventId);
        }
    }

    [Fact(
        DisplayName =
            "インメモリストーリーテスト 。並列でたくさん動かしたらどうなるか。 Versionの重複が発生しないことを確認")]
    public async Task AsynchronousInMemoryExecutionTestAsync()
    {
        // create recent activity
        var createRecentActivityResult =
            await _aggregateCommandExecutor
                .ExecCreateCommandAsync<RecentInMemoryActivity, RecentInMemoryActivityDto,
                    CreateRecentInMemoryActivity>(
                    new CreateRecentInMemoryActivity(Guid.NewGuid()));

        var recentActivityList =
            (await _aggregateService
                .DtoListAsync<RecentInMemoryActivity, RecentInMemoryActivityDto>()).ToList();
        Assert.Single(recentActivityList);
        var version = createRecentActivityResult.AggregateDto!.Version;
        var tasks = new List<Task>();
        var count = 100;
        foreach (var i in Enumerable.Range(0, count))
        {
            tasks.Add(
                Task.Run(
                    async () =>
                    {
                        var recentActivityAddedResult = await
                            _aggregateCommandExecutor
                                .ExecChangeCommandAsync<RecentInMemoryActivity,
                                    RecentInMemoryActivityDto, AddRecentInMemoryActivity>(
                                    new AddRecentInMemoryActivity(
                                        createRecentActivityResult!.AggregateDto!.AggregateId,
                                        $"Message - {i + 1}") { ReferenceVersion = version });
                        version = recentActivityAddedResult.AggregateDto!.Version;
                        _testOutputHelper.WriteLine(
                            $"{i} - {recentActivityAddedResult.AggregateDto.Version.ToString()}");
                    }));
        }
        await Task.WhenAll(tasks);
        recentActivityList =
            (await _aggregateService
                .DtoListAsync<RecentInMemoryActivity, RecentInMemoryActivityDto>()).ToList();
        Assert.Single(recentActivityList);

        var aggregateRecentActivity =
            await _aggregateService
                .GetAggregateFromInitialDefaultAggregateDtoAsync<RecentInMemoryActivity,
                    RecentInMemoryActivityDto>(
                    createRecentActivityResult.AggregateDto.AggregateId);
        var aggregateRecentActivity2 =
            await _aggregateService
                .GetAggregateDtoAsync<RecentInMemoryActivity, RecentInMemoryActivityDto>(
                    createRecentActivityResult.AggregateDto.AggregateId);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + 1, aggregateRecentActivity!.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);

        var snapshotManager =
            await _aggregateService
                .GetAggregateFromInitialDefaultAggregateDtoAsync<SnapshotManager,
                    SnapshotManagerDto>(SnapshotManager.SharedId);
        _testOutputHelper.WriteLine("-requests-");
        foreach (var key in snapshotManager!.Requests)
        {
            _testOutputHelper.WriteLine(key);
        }
        _testOutputHelper.WriteLine("-request takens-");
        foreach (var key in snapshotManager!.RequestTakens)
        {
            _testOutputHelper.WriteLine(key);
        }
    }

    [Fact(
        DisplayName =
            "CosmosDb ストーリーテスト 。一度アプリ終了してから正しく動くか"
        // , Skip = "一度終了してから実行するため、自動実行はしない"
    )]
    public async Task ContinuousExecutionTestAsync()
    {
        var recentActivityList =
            (await _aggregateService.DtoListAsync<RecentActivity, RecentActivityDto>()).ToList();
        Assert.Single(recentActivityList);
        var aggregateId = recentActivityList.First().AggregateId;

        var aggregate =
            await _aggregateService.GetAggregateAsync<RecentActivity, RecentActivityDto>(
                aggregateId);
        Assert.NotNull(aggregate);

        var aggregateRecentActivity2 =
            await _aggregateService.GetAggregateDtoAsync<RecentActivity, RecentActivityDto>(
                aggregateId);
        //var aggregateRecentActivity =
        //    await _aggregateService
        //        .GetAggregateFromInitialDefaultAggregateDtoAsync<RecentActivity, RecentActivityDto>(
        //            aggregateId);
        //Assert.Single(recentActivityList);
        //Assert.NotNull(aggregateRecentActivity);
        //Assert.NotNull(aggregateRecentActivity2);
        //Assert.Equal(aggregateRecentActivity!.Version, aggregateRecentActivity2!.Version);

        var version = recentActivityList.First().Version;
        var tasks = new List<Task>();
        var count = 80;
        foreach (var i in Enumerable.Range(0, count))
        {
            tasks.Add(
                Task.Run(
                    async () =>
                    {
                        var recentActivityAddedResult = await
                            _aggregateCommandExecutor
                                .ExecChangeCommandAsync<RecentActivity, RecentActivityDto,
                                    AddRecentActivity>(
                                    new AddRecentActivity(
                                        aggregateId,
                                        $"Message - {i + 1}") { ReferenceVersion = version });
                        version = recentActivityAddedResult.AggregateDto!.Version;
                    }));
        }
        await Task.WhenAll(tasks);
        Console.WriteLine("---list---");
        recentActivityList =
            (await _aggregateService.DtoListAsync<RecentActivity, RecentActivityDto>()).ToList();
        Assert.Single(recentActivityList);

        Console.WriteLine("---checking---");

        var snapshots = await _documentPersistentRepository.GetSnapshotsForAggregateAsync(
            aggregateId,
            typeof(RecentActivity));

        foreach (var snapshot in snapshots.OrderBy(m => m.SavedVersion))
        {
            var dto = snapshot.ToDto<RecentActivityDto>();
            var fromInitial =
                await _aggregateService
                    .GetAggregateFromInitialDefaultAggregateDtoAsync<RecentActivity,
                        RecentActivityDto>(
                        aggregateId,
                        dto.Version);
            Assert.Equal(fromInitial.Version, dto.Version);
            Assert.Equal(fromInitial.LastEventId, dto.LastEventId);
        }

        // this works
        var aggregateRecentActivity =
            await _aggregateService
                .GetAggregateFromInitialDefaultAggregateDtoAsync<RecentActivity, RecentActivityDto>(
                    aggregateId);
        Console.WriteLine("---checking 2---");
        aggregateRecentActivity2 =
            await _aggregateService.GetAggregateDtoAsync<RecentActivity, RecentActivityDto>(
                aggregateId);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + aggregate!.ToDto().Version, aggregateRecentActivity!.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);

        var snapshotManager =
            await _aggregateService
                .GetAggregateFromInitialDefaultAggregateDtoAsync<SnapshotManager,
                    SnapshotManagerDto>(SnapshotManager.SharedId);
        _testOutputHelper.WriteLine("-requests-");
        foreach (var key in snapshotManager!.Requests)
        {
            _testOutputHelper.WriteLine(key);
        }
        _testOutputHelper.WriteLine("-request takens-");
        foreach (var key in snapshotManager!.RequestTakens)
        {
            _testOutputHelper.WriteLine(key);
        }
    }
}
