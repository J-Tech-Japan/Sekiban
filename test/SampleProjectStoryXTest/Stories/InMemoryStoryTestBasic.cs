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
using CustomerDomainContext.Projections;
using CustomerDomainContext.Shared.Exceptions;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Queries;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.Shared.Exceptions;
using Sekiban.EventSourcing.Snapshots;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest.Stories;

public class InMemoryStoryTestBasic : ByTestTestBase
{
    private readonly IAggregateCommandExecutor _aggregateCommandExecutor;
    private readonly ISingleAggregateService _aggregateService;
    private readonly MultipleAggregateProjectionService _multipleAggregateProjectionService;
    private readonly ITestOutputHelper _testOutputHelper;

    public InMemoryStoryTestBasic(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        _aggregateCommandExecutor = GetService<IAggregateCommandExecutor>();
        _aggregateService = GetService<ISingleAggregateService>();
        _multipleAggregateProjectionService = GetService<MultipleAggregateProjectionService>();
        // create recent activity
        _aggregateCommandExecutor
            .ExecCreateCommandAsync<SnapshotManager, SnapshotManagerDto, CreateSnapshotManager>(new CreateSnapshotManager(SnapshotManager.SharedId))
            .Wait();
    }
    [Fact(DisplayName = "CosmosDb ストーリーテスト インメモリで集約の機能のテストを行う")]
    public async Task CosmosDbStory()
    {
        // create list branch
        var branchList = await _multipleAggregateProjectionService.GetAggregateList<Branch, BranchDto>();
        Assert.Empty(branchList);
        var branchResult = await _aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchDto, CreateBranch>(new CreateBranch("Japan"));
        Assert.NotNull(branchResult);
        Assert.NotNull(branchResult.AggregateDto);
        var branchId = branchResult.AggregateDto!.AggregateId;
        branchList = await _multipleAggregateProjectionService.GetAggregateList<Branch, BranchDto>();
        Assert.Single(branchList);
        var branchFromList = branchList.First(m => m.AggregateId == branchId);
        Assert.NotNull(branchFromList);

        var branchResult2 = await _aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchDto, CreateBranch>(new CreateBranch("USA"));
        branchList = await _multipleAggregateProjectionService.GetAggregateList<Branch, BranchDto>();
        Assert.Equal(2, branchList.Count);
        var branchListFromMultiple = await _multipleAggregateProjectionService.GetAggregateList<Branch, BranchDto>();
        Assert.Equal(2, branchListFromMultiple.Count);

        // loyalty point should be []  
        var loyaltyPointList = await _multipleAggregateProjectionService.GetAggregateList<LoyaltyPoint, LoyaltyPointDto>();
        Assert.Empty(loyaltyPointList);

        var clientNameList = await _multipleAggregateProjectionService.GetSingleAggregateProjectionList<ClientNameHistoryProjection>();
        Assert.Empty(clientNameList);

        // create client
        var clientList = await _multipleAggregateProjectionService.GetAggregateList<Client, ClientDto>();
        Assert.Empty(clientList);
        var originalName = "Tanaka Taro";
        var createClientResult
            = await _aggregateCommandExecutor.ExecCreateCommandAsync<Client, ClientDto, CreateClient>(
                new CreateClient(branchId, originalName, "tanaka@example.com"));
        Assert.NotNull(createClientResult);
        Assert.NotNull(createClientResult.AggregateDto);
        var clientId = createClientResult.AggregateDto!.AggregateId;
        clientList = await _multipleAggregateProjectionService.GetAggregateList<Client, ClientDto>();
        Assert.Single(clientList);

        // singleAggregateProjection
        clientNameList = await _multipleAggregateProjectionService.GetSingleAggregateProjectionList<ClientNameHistoryProjection>();
        Assert.Single(clientNameList);
        var tanakaProjection = clientNameList.First(m => m.AggregateId == clientId);
        Assert.Single(tanakaProjection.ClientNames);
        Assert.Equal(originalName, tanakaProjection.ClientNames.First().Name);

        var clientNameListFromMultiple = await _multipleAggregateProjectionService.GetSingleAggregateProjectionList<ClientNameHistoryProjection>();
        Assert.Single(clientNameListFromMultiple);
        Assert.Equal(clientNameList.First().AggregateId, clientNameListFromMultiple.First().AggregateId);

        var secondName = "田中 太郎";

        // should throw version error 
        await Assert.ThrowsAsync<JJAggregateCommandInconsistentVersionException>(
            async () =>
            {
                await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ClientDto, ChangeClientName>(
                    new ChangeClientName(clientId, secondName));
            });
        // change name
        var changeNameResult = await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ClientDto, ChangeClientName>(
            new ChangeClientName(clientId, secondName) { ReferenceVersion = createClientResult.AggregateDto!.Version });

        // change name projection
        clientNameList = await _multipleAggregateProjectionService.GetSingleAggregateProjectionList<ClientNameHistoryProjection>();
        Assert.Single(clientNameList);
        tanakaProjection = clientNameList.First(m => m.AggregateId == clientId);
        Assert.Equal(2, tanakaProjection.ClientNames.Count);
        Assert.Equal(originalName, tanakaProjection.ClientNames.First().Name);
        Assert.Equal(secondName, tanakaProjection.ClientNames[1].Name);

        // test change name multiple time to create projection 
        var versionCN = changeNameResult!.AggregateDto!.Version;
        var countChangeName = 60;
        foreach (var i in Enumerable.Range(0, countChangeName))
        {
            var changeNameResult2 = await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ClientDto, ChangeClientName>(
                new ChangeClientName(clientId, $"newname - {i + 1}") { ReferenceVersion = versionCN });
            versionCN = changeNameResult2.AggregateDto!.Version;
        }

        // get change name dto
        var changeNameProjection = await _aggregateService.GetProjectionAsync<ClientNameHistoryProjection>(clientId);
        Assert.NotNull(changeNameProjection);

        // loyalty point should be created with event subscribe
        loyaltyPointList = await _multipleAggregateProjectionService.GetAggregateList<LoyaltyPoint, LoyaltyPointDto>();
        Assert.Single(clientList);
        // first point = 0
        var loyaltyPoint = await _aggregateService.GetAggregateDtoAsync<LoyaltyPoint, LoyaltyPointDto>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(0, loyaltyPoint!.CurrentPoint);

        var datetimeFirst = DateTime.Now;
        var addPointResult = await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointDto, AddLoyaltyPoint>(
            new AddLoyaltyPoint(clientId, datetimeFirst, LoyaltyPointReceiveTypeKeys.FlightDomestic, 1000, "")
            {
                ReferenceVersion = loyaltyPoint.Version
            });
        Assert.NotNull(addPointResult);
        Assert.NotNull(addPointResult.AggregateDto);

        loyaltyPoint = await _aggregateService.GetAggregateDtoAsync<LoyaltyPoint, LoyaltyPointDto>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(1000, loyaltyPoint!.CurrentPoint);

        // should throw not enough point error 
        await Assert.ThrowsAsync<JJLoyaltyPointNotEnoughException>(
            async () =>
            {
                await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointDto, UseLoyaltyPoint>(
                    new UseLoyaltyPoint(clientId, datetimeFirst.AddSeconds(1), LoyaltyPointUsageTypeKeys.FlightUpgrade, 2000, "")
                    {
                        ReferenceVersion = addPointResult!.AggregateDto!.Version
                    });
            });
        var usePointResult = await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointDto, UseLoyaltyPoint>(
            new UseLoyaltyPoint(clientId, DateTime.Now, LoyaltyPointUsageTypeKeys.FlightUpgrade, 200, "")
            {
                ReferenceVersion = addPointResult!.AggregateDto!.Version
            });
        Assert.NotNull(usePointResult);
        Assert.NotNull(usePointResult.AggregateDto);

        loyaltyPoint = await _aggregateService.GetAggregateDtoAsync<LoyaltyPoint, LoyaltyPointDto>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(800, loyaltyPoint!.CurrentPoint);

        var p = await _multipleAggregateProjectionService.GetProjectionAsync<ClientLoyaltyPointMultipleProjection>();
        Assert.NotNull(p);
        Assert.Equal(2, p.Branches.Count);
        Assert.Single(p.Records);

        // delete client
        var deleteClientResult = await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ClientDto, DeleteClient>(
            new DeleteClient(clientId) { ReferenceVersion = versionCN });
        Assert.NotNull(deleteClientResult);
        Assert.NotNull(deleteClientResult.AggregateDto);
        // client deleted
        clientList = await _multipleAggregateProjectionService.GetAggregateList<Client, ClientDto>();
        Assert.Empty(clientList);
        // can find deleted client
        clientList = await _multipleAggregateProjectionService.GetAggregateList<Client, ClientDto>(QueryListType.DeletedOnly);
        Assert.Single(clientList);
        clientList = await _multipleAggregateProjectionService.GetAggregateList<Client, ClientDto>(QueryListType.ActiveAndDeleted);
        Assert.Single(clientList);

        // loyalty point should be created with event subscribe
        loyaltyPointList = await _multipleAggregateProjectionService.GetAggregateList<LoyaltyPoint, LoyaltyPointDto>();
        Assert.Empty(loyaltyPointList);
        loyaltyPointList = await _multipleAggregateProjectionService.GetAggregateList<LoyaltyPoint, LoyaltyPointDto>(QueryListType.DeletedOnly);
        Assert.Single(loyaltyPointList);

        // create recent activity
        var createRecentActivityResult
            = await _aggregateCommandExecutor.ExecCreateCommandAsync<RecentActivity, RecentActivityDto, CreateRecentActivity>(
                new CreateRecentActivity(Guid.NewGuid()));

        var recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity, RecentActivityDto>();
        Assert.Single(recentActivityList);
        var version = createRecentActivityResult.AggregateDto!.Version;
        var count = 60;
        foreach (var i in Enumerable.Range(0, count))
        {
            var recentActivityAddedResult
                = await _aggregateCommandExecutor.ExecChangeCommandAsync<RecentActivity, RecentActivityDto, AddRecentActivity>(
                    new AddRecentActivity(createRecentActivityResult!.AggregateDto!.AggregateId, $"Message - {i + 1}")
                    {
                        ReferenceVersion = version
                    });
            version = recentActivityAddedResult.AggregateDto!.Version;
        }
        recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity, RecentActivityDto>();
        Assert.Single(recentActivityList);
        Assert.Equal(count + 1, version);

        p = await _multipleAggregateProjectionService.GetProjectionAsync<ClientLoyaltyPointMultipleProjection>();
        Assert.NotNull(p);
        Assert.Equal(2, p.Branches.Count);
        Assert.Empty(p.Records);
        var snapshotManager
            = await _aggregateService.GetAggregateFromInitialDefaultAggregateDtoAsync<SnapshotManager, SnapshotManagerDto>(SnapshotManager.SharedId);
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

    [Fact(DisplayName = "CosmosDb ストーリーテスト 。並列でたくさん動かしたらどうなるか。 INoValidateCommand がRecentActivityに適応されているので、問題ないはず")]
    public async Task AsynchronousExecutionTestAsync()
    {
        // create recent activity
        var createRecentActivityResult
            = await _aggregateCommandExecutor.ExecCreateCommandAsync<RecentActivity, RecentActivityDto, CreateRecentActivity>(
                new CreateRecentActivity(Guid.NewGuid()));

        var recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity, RecentActivityDto>();
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
                            = await _aggregateCommandExecutor.ExecChangeCommandAsync<RecentActivity, RecentActivityDto, AddRecentActivity>(
                                new AddRecentActivity(createRecentActivityResult!.AggregateDto!.AggregateId, $"Message - {i + 1}")
                                {
                                    ReferenceVersion = version
                                });
                        version = recentActivityAddedResult.AggregateDto!.Version;
                    }));
        }
        await Task.WhenAll(tasks);
        recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity, RecentActivityDto>();
        Assert.Single(recentActivityList);
        // this works
        var aggregateRecentActivity
            = await _aggregateService.GetAggregateFromInitialDefaultAggregateDtoAsync<RecentActivity, RecentActivityDto>(
                createRecentActivityResult.AggregateDto.AggregateId);
        var aggregateRecentActivity2
            = await _aggregateService.GetAggregateDtoAsync<RecentActivity, RecentActivityDto>(createRecentActivityResult.AggregateDto.AggregateId);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + 1, aggregateRecentActivity!.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);

        var snapshotManager
            = await _aggregateService.GetAggregateFromInitialDefaultAggregateDtoAsync<SnapshotManager, SnapshotManagerDto>(SnapshotManager.SharedId);
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

    private async Task CheckSnapshots<T, Q>(List<SnapshotDocument> snapshots, Guid aggregateId)
        where T : TransferableAggregateBase<Q> where Q : AggregateDtoBase
    {
        foreach (var dto in snapshots.Select(snapshot => snapshot.ToDto<RecentActivityDto>()))
        {
            if (dto == null) { throw new JJInvalidArgumentException(); }
            var fromInitial = await _aggregateService.GetAggregateFromInitialDefaultAggregateDtoAsync<T, Q>(aggregateId, dto.Version);
            if (fromInitial == null) { throw new JJInvalidArgumentException(); }
            Assert.Equal(fromInitial.Version, dto.Version);
            Assert.Equal(fromInitial.LastEventId, dto.LastEventId);
        }
    }
    [Fact(DisplayName = "インメモリストーリーテスト 。並列でたくさん動かしたらどうなるか。 Versionの重複が発生しないことを確認")]
    public async Task AsynchronousInMemoryExecutionTestAsync()
    {
        // create recent activity
        var createRecentActivityResult
            = await _aggregateCommandExecutor.ExecCreateCommandAsync<RecentInMemoryActivity, RecentInMemoryActivityDto, CreateRecentInMemoryActivity>(
                new CreateRecentInMemoryActivity(Guid.NewGuid()));

        var recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentInMemoryActivity, RecentInMemoryActivityDto>();
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
                        var recentActivityAddedResult
                            = await _aggregateCommandExecutor
                                .ExecChangeCommandAsync<RecentInMemoryActivity, RecentInMemoryActivityDto, AddRecentInMemoryActivity>(
                                    new AddRecentInMemoryActivity(createRecentActivityResult!.AggregateDto!.AggregateId, $"Message - {i + 1}")
                                    {
                                        ReferenceVersion = version
                                    });
                        version = recentActivityAddedResult.AggregateDto!.Version;
                        _testOutputHelper.WriteLine($"{i} - {recentActivityAddedResult.AggregateDto.Version.ToString()}");
                    }));
        }
        await Task.WhenAll(tasks);
        recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentInMemoryActivity, RecentInMemoryActivityDto>();
        Assert.Single(recentActivityList);

        var aggregateRecentActivity
            = await _aggregateService.GetAggregateFromInitialDefaultAggregateDtoAsync<RecentInMemoryActivity, RecentInMemoryActivityDto>(
                createRecentActivityResult.AggregateDto.AggregateId);
        var aggregateRecentActivity2
            = await _aggregateService.GetAggregateDtoAsync<RecentInMemoryActivity, RecentInMemoryActivityDto>(
                createRecentActivityResult.AggregateDto.AggregateId);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + 1, aggregateRecentActivity!.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);

        var snapshotManager
            = await _aggregateService.GetAggregateFromInitialDefaultAggregateDtoAsync<SnapshotManager, SnapshotManagerDto>(SnapshotManager.SharedId);
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