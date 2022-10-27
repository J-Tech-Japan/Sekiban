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
        var branchList = await _multipleAggregateProjectionService.GetAggregateList<Branch>();
        Assert.Empty(branchList);
        var (branchResult, events)
            = await _aggregateCommandExecutor.ExecCreateCommandAsync<Branch, CreateBranch>(new CreateBranch("Japan"));
        var branchId = branchResult.AggregateId;
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
            .GetSingleAggregateProjectionList<Client, ClientNameHistoryProjection, ClientNameHistoryProjection.PayloadDefinition>();
        Assert.Empty(clientNameList);

        // create client
        var clientList = await _multipleAggregateProjectionService.GetAggregateList<Client>();
        Assert.Empty(clientList);
        var originalName = "Tanaka Taro";
        var (createClientResult, events2) = await _aggregateCommandExecutor.ExecCreateCommandAsync<Client, CreateClient>(
            new CreateClient(branchId!.Value, originalName, "tanaka@example.com"));
        var clientId = createClientResult.AggregateId;
        Assert.NotNull(createClientResult);
        Assert.NotNull(createClientResult.AggregateId);
        clientList = await _multipleAggregateProjectionService.GetAggregateList<Client>();
        Assert.Single(clientList);

        // singleAggregateProjection
        clientNameList = await _multipleAggregateProjectionService
            .GetSingleAggregateProjectionList<Client, ClientNameHistoryProjection, ClientNameHistoryProjection.PayloadDefinition>();
        Assert.Single(clientNameList);
        var tanakaProjection = clientNameList.First(m => m.AggregateId == clientId);
        Assert.Single(tanakaProjection.Payload.ClientNames);
        Assert.Equal(originalName, tanakaProjection.Payload.ClientNames.ToList().First().Name);

        var clientNameListFromMultiple = await _multipleAggregateProjectionService
            .GetSingleAggregateProjectionList<Client, ClientNameHistoryProjection, ClientNameHistoryProjection.PayloadDefinition>();
        Assert.Single(clientNameListFromMultiple);
        Assert.Equal(clientNameList.First().AggregateId, clientNameListFromMultiple.First().AggregateId);

        var secondName = "田中 太郎";

        // should throw version error 
        await Assert.ThrowsAsync<SekibanAggregateCommandInconsistentVersionException>(
            async () =>
            {
                await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ChangeClientName>(
                    new ChangeClientName(clientId!.Value, secondName));
            });
        // change name
        var (changeNameResult, events3) = await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ChangeClientName>(
            new ChangeClientName(clientId!.Value, secondName) { ReferenceVersion = createClientResult.Version });

        // change name projection
        clientNameList = await _multipleAggregateProjectionService
            .GetSingleAggregateProjectionList<Client, ClientNameHistoryProjection, ClientNameHistoryProjection.PayloadDefinition>();
        Assert.Single(clientNameList);
        tanakaProjection = clientNameList.First(m => m.AggregateId == clientId);
        Assert.Equal(2, tanakaProjection.Payload.ClientNames.Count);
        Assert.Equal(originalName, tanakaProjection.Payload.ClientNames.First().Name);
        Assert.Equal(secondName, tanakaProjection.Payload.ClientNames.ToList()[1].Name);

        // test change name multiple time to create projection 
        var versionCN = changeNameResult.Version;
        var countChangeName = 60;
        foreach (var i in Enumerable.Range(0, countChangeName))
        {
            var (changeNameResult2, events4) = await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, ChangeClientName>(
                new ChangeClientName(clientId!.Value, $"newname - {i + 1}") { ReferenceVersion = versionCN });
            versionCN = changeNameResult2.Version;
        }

        // get change name state
        var changeNameProjection
            = await _aggregateService.GetProjectionAsync<Client, ClientNameHistoryProjection, ClientNameHistoryProjection.PayloadDefinition>(
                clientId!.Value);
        Assert.NotNull(changeNameProjection);

        // loyalty point should be created with event subscribe
        loyaltyPointList = await _multipleAggregateProjectionService.GetAggregateList<LoyaltyPoint>();
        Assert.Single(clientList);
        // first point = 0
        var loyaltyPoint = await _aggregateService.GetAggregateStateAsync<LoyaltyPoint>(clientId!.Value);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(0, loyaltyPoint!.Payload.CurrentPoint);

        var datetimeFirst = DateTime.Now;
        var (addPointResult, events5) = await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, AddLoyaltyPoint>(
            new AddLoyaltyPoint(clientId!.Value, datetimeFirst, LoyaltyPointReceiveTypeKeys.FlightDomestic, 1000, "")
            {
                ReferenceVersion = loyaltyPoint.Version
            });
        Assert.NotNull(addPointResult);
        Assert.NotNull(addPointResult.AggregateId);

        loyaltyPoint = await _aggregateService.GetAggregateStateAsync<LoyaltyPoint>(clientId!.Value);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(1000, loyaltyPoint!.Payload.CurrentPoint);

        // should throw not enough point error 
        await Assert.ThrowsAsync<SekibanLoyaltyPointNotEnoughException>(
            async () =>
            {
                await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, UseLoyaltyPoint>(
                    new UseLoyaltyPoint(clientId!.Value, datetimeFirst.AddSeconds(1), LoyaltyPointUsageTypeKeys.FlightUpgrade, 2000, "")
                    {
                        ReferenceVersion = addPointResult.Version
                    });
            });
        var (usePointResult, events6) = await _aggregateCommandExecutor.ExecChangeCommandAsync<LoyaltyPoint, UseLoyaltyPoint>(
            new UseLoyaltyPoint(clientId.Value, DateTime.Now, LoyaltyPointUsageTypeKeys.FlightUpgrade, 200, "")
            {
                ReferenceVersion = addPointResult.Version
            });
        Assert.NotNull(usePointResult);
        Assert.NotNull(usePointResult.AggregateId);

        loyaltyPoint = await _aggregateService.GetAggregateStateAsync<LoyaltyPoint>(clientId.Value);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(800, loyaltyPoint!.Payload.CurrentPoint);

        var p = await _multipleAggregateProjectionService
            .GetProjectionAsync<ClientLoyaltyPointMultipleProjection, ClientLoyaltyPointMultipleProjection.PayloadDefinition>();
        Assert.NotNull(p);
        Assert.Equal(2, p.Payload.Branches.Count);
        Assert.Single(p.Payload.Records);

        // delete client
        var (deleteClientResult, events7) = await _aggregateCommandExecutor.ExecChangeCommandAsync<Client, DeleteClient>(
            new DeleteClient(clientId.Value) { ReferenceVersion = versionCN });
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
        var (createRecentActivityResult, events8)
            = await _aggregateCommandExecutor.ExecCreateCommandAsync<RecentActivity, CreateRecentActivity>(
                new CreateRecentActivity());
        var recentActivityId = createRecentActivityResult.AggregateId;

        var recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);
        var version = createRecentActivityResult.Version;
        var count = 60;
        foreach (var i in Enumerable.Range(0, count))
        {
            var (recentActivityAddedResult, events9)
                = await _aggregateCommandExecutor.ExecChangeCommandAsync<RecentActivity, AddRecentActivity>(
                    new AddRecentActivity(createRecentActivityResult.AggregateId!.Value, $"Message - {i + 1}") { ReferenceVersion = version });
            version = recentActivityAddedResult.Version;
        }
        recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);
        Assert.Equal(count + 1, version);

        p = await _multipleAggregateProjectionService
            .GetProjectionAsync<ClientLoyaltyPointMultipleProjection, ClientLoyaltyPointMultipleProjection.PayloadDefinition>();
        Assert.NotNull(p);
        Assert.Equal(2, p.Payload.Branches.Count);
        Assert.Empty(p.Payload.Records);
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

    [Fact(DisplayName = "CosmosDb ストーリーテスト 。並列でたくさん動かしたらどうなるか。 INoValidateCommand がRecentActivityに適応されているので、問題ないはず")]
    public async Task AsynchronousExecutionTestAsync()
    {
        var recentActivityId = Guid.NewGuid();
        // create recent activity
        var (createRecentActivityResult, events)
            = await _aggregateCommandExecutor.ExecCreateCommandAsync<RecentActivity, CreateRecentActivity>(
                new CreateRecentActivity());

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
                        var (recentActivityAddedResult, events2)
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
    }

    private async Task CheckSnapshots<TAggregatePayload>(List<SnapshotDocument> snapshots, Guid aggregateId)
        where TAggregatePayload : IAggregatePayload, new()
    {
        foreach (var state in snapshots.Select(snapshot => snapshot.ToState<AggregateState<TAggregatePayload>>()))
        {
            if (state is null)
            {
                throw new SekibanInvalidArgumentException();
            }
            var fromInitial = await _aggregateService.GetAggregateStateFromInitialAsync<TAggregatePayload>(aggregateId, state.Version);
            if (fromInitial is null) { throw new SekibanInvalidArgumentException(); }
            Assert.Equal(fromInitial.Version, state.Version);
            Assert.Equal(fromInitial.LastEventId, state.LastEventId);
        }
    }
    [Fact(DisplayName = "インメモリストーリーテスト 。並列でたくさん動かしたらどうなるか。 Versionの重複が発生しないことを確認")]
    public async Task AsynchronousInMemoryExecutionTestAsync()
    {
        // create recent activity
        var (createRecentActivityResult, events)
            = await _aggregateCommandExecutor
                .ExecCreateCommandAsync<RecentInMemoryActivity, CreateRecentInMemoryActivity>(
                    new CreateRecentInMemoryActivity());

        var recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentInMemoryActivity>();
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
                                .ExecChangeCommandAsync<RecentInMemoryActivity, AddRecentInMemoryActivity>(
                                    new AddRecentInMemoryActivity(createRecentActivityResult!.AggregateId!.Value, $"Message - {i + 1}")
                                    {
                                        ReferenceVersion = version
                                    });
                        version = recentActivityAddedResult.Version;
                        _testOutputHelper.WriteLine($"{i} - {recentActivityAddedResult.Version.ToString()}");
                    }));
        }
        await Task.WhenAll(tasks);
        recentActivityList = await _multipleAggregateProjectionService.GetAggregateList<RecentInMemoryActivity>();
        Assert.Single(recentActivityList);

        var aggregateRecentActivity
            = await _aggregateService.GetAggregateStateFromInitialAsync<RecentInMemoryActivity>(
                createRecentActivityResult.AggregateId!.Value);
        var aggregateRecentActivity2
            = await _aggregateService.GetAggregateStateAsync<RecentInMemoryActivity>(
                createRecentActivityResult.AggregateId!.Value);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + 1, aggregateRecentActivity!.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);
    }
}
