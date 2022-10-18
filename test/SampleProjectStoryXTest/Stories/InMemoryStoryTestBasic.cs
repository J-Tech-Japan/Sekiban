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
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query;
using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.SingleAggregate;
using Sekiban.Core.Snapshot;
using Sekiban.Core.Snapshot.Aggregate;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest.Stories;

public class InMemoryStoryTestBasic : ProjectSekibanByTestTestBase
{
    private readonly IAggregateCommandExecutor _aggregateCommandExecutor;
    private readonly ISingleAggregateService _aggregateService;
    private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    private readonly ITestOutputHelper _testOutputHelper;

    public InMemoryStoryTestBasic(ITestOutputHelper testOutputHelper, bool inMemory = true) : base(inMemory)
    {
        _testOutputHelper = testOutputHelper;
        _aggregateCommandExecutor = GetService<IAggregateCommandExecutor>();
        _aggregateService = GetService<ISingleAggregateService>();
        _multipleAggregateProjectionService = GetService<IMultipleAggregateProjectionService>();
    }
    [Fact(DisplayName = "CosmosDb ストーリーテスト インメモリで集約の機能のテストを行う")]
    public async Task CosmosDbStory()
    {
        // create list branch
        var branchList = await _multipleAggregateProjectionService.GetAggregateList<Branch, BranchContents>();
        Assert.Empty(branchList);
        var (branchResult, events)
            = await _aggregateCommandExecutor.ExecCreateCommandAsync<Branch, BranchContents, CreateBranch>(new CreateBranch("Japan"));
        var branchId = branchResult.AggregateId;
        Assert.NotNull(branchResult);
        Assert.NotNull(branchResult.AggregateId);
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
        var (createClientResult, events2) = await _aggregateCommandExecutor.ExecCreateCommandAsync<Client, ClientContents, CreateClient>(
            new CreateClient(branchId!.Value, originalName, "tanaka@example.com"));
        var clientId = createClientResult.AggregateId;
        Assert.NotNull(createClientResult);
        Assert.NotNull(createClientResult.AggregateId);
        clientList = await _multipleAggregateProjectionService.GetAggregateList<Client, ClientContents>();
        Assert.Single(clientList);

        // singleAggregateProjection
        clientNameList = await _multipleAggregateProjectionService
            .GetSingleAggregateProjectionList<Client, ClientNameHistoryProjection, ClientNameHistoryProjection.ContentsDefinition>();
        Assert.Single(clientNameList);
        var tanakaProjection = clientNameList.First(m => m.AggregateId == clientId);
        Assert.Single(tanakaProjection.Contents.ClientNames);
        Assert.Equal(originalName, tanakaProjection.Contents.ClientNames.ToList().First().Name);

        var clientNameListFromMultiple = await _multipleAggregateProjectionService
            .GetSingleAggregateProjectionList<Client, ClientNameHistoryProjection, ClientNameHistoryProjection.ContentsDefinition>();
        Assert.Single(clientNameListFromMultiple);
        Assert.Equal(clientNameList.First().AggregateId, clientNameListFromMultiple.First().AggregateId);

        var secondName = "田中 太郎";

        // should throw version error 
        await Assert.ThrowsAsync<SekibanAggregateCommandInconsistentVersionException>(
            async () =>
            {
                await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ClientContents, ChangeClientName>(
                    new ChangeClientName(clientId!.Value, secondName));
            });
        // change name
        var (changeNameResult, events3) = await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ClientContents, ChangeClientName>(
            new ChangeClientName(clientId!.Value, secondName) { ReferenceVersion = createClientResult.Version });

        // change name projection
        clientNameList = await _multipleAggregateProjectionService
            .GetSingleAggregateProjectionList<Client, ClientNameHistoryProjection, ClientNameHistoryProjection.ContentsDefinition>();
        Assert.Single(clientNameList);
        tanakaProjection = clientNameList.First(m => m.AggregateId == clientId);
        Assert.Equal(2, tanakaProjection.Contents.ClientNames.Count);
        Assert.Equal(originalName, tanakaProjection.Contents.ClientNames.First().Name);
        Assert.Equal(secondName, tanakaProjection.Contents.ClientNames.ToList()[1].Name);

        // test change name multiple time to create projection 
        var versionCN = changeNameResult.Version;
        var countChangeName = 60;
        foreach (var i in Enumerable.Range(0, countChangeName))
        {
            var (changeNameResult2, events4) = await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ClientContents, ChangeClientName>(
                new ChangeClientName(clientId!.Value, $"newname - {i + 1}") { ReferenceVersion = versionCN });
            versionCN = changeNameResult2.Version;
        }

        // get change name dto
        var changeNameProjection
            = await _aggregateService.GetProjectionAsync<Client, ClientNameHistoryProjection, ClientNameHistoryProjection.ContentsDefinition>(
                clientId!.Value);
        Assert.NotNull(changeNameProjection);

        // loyalty point should be created with event subscribe
        loyaltyPointList = await _multipleAggregateProjectionService.GetAggregateList<LoyaltyPoint, LoyaltyPointContents>();
        Assert.Single(clientList);
        // first point = 0
        var loyaltyPoint = await _aggregateService.GetAggregateDtoAsync<LoyaltyPoint, LoyaltyPointContents>(clientId!.Value);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(0, loyaltyPoint!.Contents.CurrentPoint);

        var datetimeFirst = DateTime.Now;
        var (addPointResult, events5) = await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointContents, AddLoyaltyPoint>(
            new AddLoyaltyPoint(clientId!.Value, datetimeFirst, LoyaltyPointReceiveTypeKeys.FlightDomestic, 1000, "")
            {
                ReferenceVersion = loyaltyPoint.Version
            });
        Assert.NotNull(addPointResult);
        Assert.NotNull(addPointResult.AggregateId);

        loyaltyPoint = await _aggregateService.GetAggregateDtoAsync<LoyaltyPoint, LoyaltyPointContents>(clientId!.Value);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(1000, loyaltyPoint!.Contents.CurrentPoint);

        // should throw not enough point error 
        await Assert.ThrowsAsync<SekibanLoyaltyPointNotEnoughException>(
            async () =>
            {
                await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointContents, UseLoyaltyPoint>(
                    new UseLoyaltyPoint(clientId!.Value, datetimeFirst.AddSeconds(1), LoyaltyPointUsageTypeKeys.FlightUpgrade, 2000, "")
                    {
                        ReferenceVersion = addPointResult.Version
                    });
            });
        var (usePointResult, events6) = await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, LoyaltyPointContents, UseLoyaltyPoint>(
            new UseLoyaltyPoint(clientId.Value, DateTime.Now, LoyaltyPointUsageTypeKeys.FlightUpgrade, 200, "")
            {
                ReferenceVersion = addPointResult.Version
            });
        Assert.NotNull(usePointResult);
        Assert.NotNull(usePointResult.AggregateId);

        loyaltyPoint = await _aggregateService.GetAggregateDtoAsync<LoyaltyPoint, LoyaltyPointContents>(clientId.Value);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(800, loyaltyPoint!.Contents.CurrentPoint);

        var p = await _multipleAggregateProjectionService
            .GetProjectionAsync<ClientLoyaltyPointMultipleProjection, ClientLoyaltyPointMultipleProjection.ContentsDefinition>();
        Assert.NotNull(p);
        Assert.Equal(2, p.Contents.Branches.Count);
        Assert.Single(p.Contents.Records);

        // delete client
        var (deleteClientResult, events7) = await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ClientContents, DeleteClient>(
            new DeleteClient(clientId.Value) { ReferenceVersion = versionCN });
        Assert.NotNull(deleteClientResult);
        Assert.NotNull(deleteClientResult.AggregateId);
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
        var (createRecentActivityResult, events8)
            = await _aggregateCommandExecutor.ExecCreateCommandAsync<RecentActivity, RecentActivityContents, CreateRecentActivity>(
                new CreateRecentActivity());
        var recentActivityId = createRecentActivityResult.AggregateId;

        var recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity, RecentActivityContents>();
        Assert.Single(recentActivityList);
        var version = createRecentActivityResult.Version;
        var count = 60;
        foreach (var i in Enumerable.Range(0, count))
        {
            var (recentActivityAddedResult, events9)
                = await _aggregateCommandExecutor.ExecChangeCommandAsync<RecentActivity, RecentActivityContents, AddRecentActivity>(
                    new AddRecentActivity(createRecentActivityResult.AggregateId!.Value, $"Message - {i + 1}") { ReferenceVersion = version });
            version = recentActivityAddedResult.Version;
        }
        recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity, RecentActivityContents>();
        Assert.Single(recentActivityList);
        Assert.Equal(count + 1, version);

        p = await _multipleAggregateProjectionService
            .GetProjectionAsync<ClientLoyaltyPointMultipleProjection, ClientLoyaltyPointMultipleProjection.ContentsDefinition>();
        Assert.NotNull(p);
        Assert.Equal(2, p.Contents.Branches.Count);
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
    }

    [Fact(DisplayName = "CosmosDb ストーリーテスト 。並列でたくさん動かしたらどうなるか。 INoValidateCommand がRecentActivityに適応されているので、問題ないはず")]
    public async Task AsynchronousExecutionTestAsync()
    {
        var recentActivityId = Guid.NewGuid();
        // create recent activity
        var (createRecentActivityResult, events)
            = await _aggregateCommandExecutor.ExecCreateCommandAsync<RecentActivity, RecentActivityContents, CreateRecentActivity>(
                new CreateRecentActivity());

        var recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity, RecentActivityContents>();
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
                        var (recentActivityAddedResult, events2)
                            = await _aggregateCommandExecutor.ExecChangeCommandAsync<RecentActivity, RecentActivityContents, AddRecentActivity>(
                                new AddRecentActivity(createRecentActivityResult.AggregateId!.Value, $"Message - {i + 1}")
                                {
                                    ReferenceVersion = version
                                });
                        version = recentActivityAddedResult.Version;
                    }));
        }
        await Task.WhenAll(tasks);
        recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity, RecentActivityContents>();
        Assert.Single(recentActivityList);
        // this works
        var aggregateRecentActivity
            = await _aggregateService.GetAggregateFromInitialDefaultAggregateDtoAsync<RecentActivity, RecentActivityContents>(
                createRecentActivityResult.AggregateId!.Value);
        var aggregateRecentActivity2
            = await _aggregateService.GetAggregateDtoAsync<RecentActivity, RecentActivityContents>(createRecentActivityResult.AggregateId!.Value);
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
    }

    private async Task CheckSnapshots<T, TContents>(List<SnapshotDocument> snapshots, Guid aggregateId) where T : AggregateBase<TContents>, new()
        where TContents : IAggregateContents, new()
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
        var (createRecentActivityResult, events)
            = await _aggregateCommandExecutor
                .ExecCreateCommandAsync<RecentInMemoryActivity, RecentInMemoryActivityContents, CreateRecentInMemoryActivity>(
                    new CreateRecentInMemoryActivity());

        var recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentInMemoryActivity, RecentInMemoryActivityContents>();
        Assert.Single(recentActivityList);
        var version = createRecentActivityResult.Version;
        var tasks = new List<Task>();
        var count = 100;
        foreach (var i in Enumerable.Range(0, count))
        {
            tasks.Add(
                Task.Run(
                    async () =>
                    {
                        var (recentActivityAddedResult, _)
                            = await _aggregateCommandExecutor
                                .ExecChangeCommandAsync<RecentInMemoryActivity, RecentInMemoryActivityContents, AddRecentInMemoryActivity>(
                                    new AddRecentInMemoryActivity(createRecentActivityResult!.AggregateId!.Value, $"Message - {i + 1}")
                                    {
                                        ReferenceVersion = version
                                    });
                        version = recentActivityAddedResult.Version;
                        _testOutputHelper.WriteLine($"{i} - {recentActivityAddedResult.Version.ToString()}");
                    }));
        }
        await Task.WhenAll(tasks);
        recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentInMemoryActivity, RecentInMemoryActivityContents>();
        Assert.Single(recentActivityList);

        var aggregateRecentActivity
            = await _aggregateService.GetAggregateFromInitialDefaultAggregateDtoAsync<RecentInMemoryActivity, RecentInMemoryActivityContents>(
                createRecentActivityResult.AggregateId!.Value);
        var aggregateRecentActivity2
            = await _aggregateService.GetAggregateDtoAsync<RecentInMemoryActivity, RecentInMemoryActivityContents>(
                createRecentActivityResult.AggregateId!.Value);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + 1, aggregateRecentActivity!.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);
    }
}
