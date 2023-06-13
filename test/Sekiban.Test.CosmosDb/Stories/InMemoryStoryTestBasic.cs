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
using Sekiban.Testing.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories;

public class InMemoryStoryTestBasic : ProjectSekibanByTestTestBase
{
    private readonly HybridStoreManager _hybridStoreManager;
    private readonly InMemoryDocumentStore _inMemoryDocumentStore;
    private readonly IMemoryCacheAccessor _memoryCacheAccessor;
    private readonly ITestOutputHelper _testOutputHelper;
    private readonly ICommandExecutor commandExecutor;
    private readonly IMultiProjectionService multiProjectionService;
    private readonly IAggregateLoader projectionService;

    public InMemoryStoryTestBasic(ITestOutputHelper testOutputHelper) : base(testOutputHelper, new InMemorySekibanServiceProviderGenerator())
    {
        _testOutputHelper = testOutputHelper;
        commandExecutor = GetService<ICommandExecutor>();
        projectionService = GetService<IAggregateLoader>();
        multiProjectionService = GetService<IMultiProjectionService>();
        _inMemoryDocumentStore = GetService<InMemoryDocumentStore>();
        _hybridStoreManager = GetService<HybridStoreManager>();
        _memoryCacheAccessor = GetService<IMemoryCacheAccessor>();
    }

    [Fact(DisplayName = "CosmosDb ストーリーテスト インメモリで集約の機能のテストを行う")]
    public async Task CosmosDbStory()
    {

        _inMemoryDocumentStore.ResetInMemoryStore();
        _hybridStoreManager.ClearHybridPartitions();
        ((MemoryCache)_memoryCacheAccessor.Cache).Compact(1);

        // create list branch
        var branchList = await multiProjectionService.GetAggregateList<Branch>();
        Assert.Empty(branchList);
        var branchResult = await commandExecutor.ExecCommandAsync(new CreateBranch("Japan"));
        var branchId = branchResult.AggregateId;
        Assert.NotNull(branchResult);
        Assert.NotNull(branchResult.AggregateId);
        branchList = await multiProjectionService.GetAggregateList<Branch>();
        Assert.Single(branchList);
        var branchFromList = branchList.First(m => m.AggregateId == branchId);
        Assert.NotNull(branchFromList);

        var branchResult2 = await commandExecutor.ExecCommandAsync(new CreateBranch("USA"));
        branchList = await multiProjectionService.GetAggregateList<Branch>();
        Assert.Equal(2, branchList.Count);
        var branchListFromMultiple = await multiProjectionService.GetAggregateList<Branch>();
        Assert.Equal(2, branchListFromMultiple.Count);

        // loyalty point should be []  
        var loyaltyPointList = await multiProjectionService.GetAggregateList<LoyaltyPoint>();
        Assert.Empty(loyaltyPointList);

        var clientNameList = await multiProjectionService.GetSingleProjectionList<ClientNameHistoryProjection>();
        Assert.Empty(clientNameList);

        // create client
        var clientList = await multiProjectionService.GetAggregateList<Client>();
        Assert.Empty(clientList);
        var originalName = "Tanaka Taro";
        var createClientResult = await commandExecutor.ExecCommandAsync(new CreateClient(branchId!.Value, originalName, "tanaka@example.com"));
        var clientId = createClientResult.AggregateId;
        Assert.NotNull(createClientResult);
        Assert.NotNull(createClientResult.AggregateId);
        clientList = await multiProjectionService.GetAggregateList<Client>();
        Assert.Single(clientList);

        // singleAggregateProjection
        clientNameList = await multiProjectionService.GetSingleProjectionList<ClientNameHistoryProjection>();
        Assert.Single(clientNameList);
        var tanakaProjection = clientNameList.First(m => m.AggregateId == clientId);
        Assert.Single(tanakaProjection.Payload.ClientNames);
        Assert.Equal(originalName, tanakaProjection.Payload.ClientNames.ToList().First().Name);

        var clientNameListFromMultiple = await multiProjectionService.GetSingleProjectionList<ClientNameHistoryProjection>();
        Assert.Single(clientNameListFromMultiple);
        Assert.Equal(clientNameList.First().AggregateId, clientNameListFromMultiple.First().AggregateId);

        var secondName = "田中 太郎";

        // should throw version error 
        await Assert.ThrowsAsync<SekibanCommandInconsistentVersionException>(
            async () =>
            {
                await commandExecutor.ExecCommandAsync(new ChangeClientName(clientId!.Value, secondName));
            });
        // change name
        var changeNameResult = await commandExecutor.ExecCommandAsync(
            new ChangeClientName(clientId!.Value, secondName) { ReferenceVersion = createClientResult.Version });

        // change name projection
        clientNameList = await multiProjectionService.GetSingleProjectionList<ClientNameHistoryProjection>();
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
            var changeNameResult2 = await commandExecutor.ExecCommandAsync(
                new ChangeClientName(clientId!.Value, $"newname - {i + 1}") { ReferenceVersion = versionCN });
            versionCN = changeNameResult2.Version;
        }

        // get change name state
        var changeNameProjection = await projectionService.AsSingleProjectionStateAsync<ClientNameHistoryProjection>(clientId!.Value);
        Assert.NotNull(changeNameProjection);

        // loyalty point should be created with event subscribe
        loyaltyPointList = await multiProjectionService.GetAggregateList<LoyaltyPoint>();
        Assert.Single(clientList);
        // first point = 0
        var loyaltyPoint = await projectionService.AsDefaultStateAsync<LoyaltyPoint>(clientId!.Value);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(0, loyaltyPoint!.Payload.CurrentPoint);

        var datetimeFirst = DateTime.Now;
        var addPointResult = await commandExecutor.ExecCommandAsync(
            new AddLoyaltyPoint(clientId!.Value, datetimeFirst, LoyaltyPointReceiveTypeKeys.FlightDomestic, 1000, "")
            {
                ReferenceVersion = loyaltyPoint.Version
            });
        Assert.NotNull(addPointResult);
        Assert.NotNull(addPointResult.AggregateId);

        loyaltyPoint = await projectionService.AsDefaultStateAsync<LoyaltyPoint>(clientId!.Value);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(1000, loyaltyPoint!.Payload.CurrentPoint);

        // should throw not enough point error 
        await Assert.ThrowsAsync<SekibanLoyaltyPointNotEnoughException>(
            async () =>
            {
                await commandExecutor.ExecCommandAsync(
                    new UseLoyaltyPoint(clientId!.Value, datetimeFirst.AddSeconds(1), LoyaltyPointUsageTypeKeys.FlightUpgrade, 2000, "")
                    {
                        ReferenceVersion = addPointResult.Version
                    });
            });
        var usePointResult = await commandExecutor.ExecCommandAsync(
            new UseLoyaltyPoint(clientId.Value, DateTime.Now, LoyaltyPointUsageTypeKeys.FlightUpgrade, 200, "")
            {
                ReferenceVersion = addPointResult.Version
            });
        Assert.NotNull(usePointResult);
        Assert.NotNull(usePointResult.AggregateId);

        loyaltyPoint = await projectionService.AsDefaultStateAsync<LoyaltyPoint>(clientId.Value);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(800, loyaltyPoint!.Payload.CurrentPoint);

        var p = await multiProjectionService.GetMultiProjectionAsync<ClientLoyaltyPointMultiProjection>();
        Assert.NotNull(p);
        Assert.Equal(2, p.Payload.Branches.Count);
        Assert.Single(p.Payload.Records);

        // delete client
        var deleteClientResult = await commandExecutor.ExecCommandAsync(new DeleteClient(clientId.Value) { ReferenceVersion = versionCN });
        Assert.NotNull(deleteClientResult);
        Assert.NotNull(deleteClientResult.AggregateId);
        // client deleted
        clientList = await multiProjectionService.GetAggregateList<Client>();
        Assert.Empty(clientList);
        // can find deleted client
        clientList = await multiProjectionService.GetAggregateList<Client>(QueryListType.DeletedOnly);
        Assert.Single(clientList);
        clientList = await multiProjectionService.GetAggregateList<Client>(QueryListType.ActiveAndDeleted);
        Assert.Single(clientList);

        // loyalty point should be created with event subscribe
        loyaltyPointList = await multiProjectionService.GetAggregateList<LoyaltyPoint>();
        Assert.Empty(loyaltyPointList);
        loyaltyPointList = await multiProjectionService.GetAggregateList<LoyaltyPoint>(QueryListType.DeletedOnly);
        Assert.Single(loyaltyPointList);

        // create recent activity
        var createRecentActivityResult = await commandExecutor.ExecCommandAsync(new CreateRecentActivity());
        var recentActivityId = createRecentActivityResult.AggregateId;

        var recentActivityList = await multiProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);
        var version = createRecentActivityResult.Version;
        var count = 60;
        foreach (var i in Enumerable.Range(0, count))
        {
            var recentActivityAddedResult = await commandExecutor.ExecCommandAsync(
                new AddRecentActivity(createRecentActivityResult.AggregateId!.Value, $"Message - {i + 1}") { ReferenceVersion = version });
            version = recentActivityAddedResult.Version;
        }

        recentActivityList = await multiProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);
        Assert.Equal(count + 1, version);

        p = await multiProjectionService.GetMultiProjectionAsync<ClientLoyaltyPointMultiProjection>();
        Assert.NotNull(p);
        Assert.Equal(2, p.Payload.Branches.Count);
        Assert.Empty(p.Payload.Records);
        var snapshotManager = await projectionService.AsDefaultStateFromInitialAsync<SnapshotManager>(SnapshotManager.SharedId);
        if (snapshotManager is not null)
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
    }

    [Fact(DisplayName = "CosmosDb ストーリーテスト 。並列でたくさん動かしたらどうなるか。 INoValidateCommand がRecentActivityに適応されているので、問題ないはず")]
    public async Task AsynchronousExecutionTestAsync()
    {
        var recentActivityId = Guid.NewGuid();
        // create recent activity
        var createRecentActivityResult = await commandExecutor.ExecCommandAsync(new CreateRecentActivity());

        var recentActivityList = await multiProjectionService.GetAggregateList<RecentActivity>();
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
                        var recentActivityAddedResult = await commandExecutor.ExecCommandAsync(
                            new AddRecentActivity(
                                createRecentActivityResult.AggregateId!.Value,
                                $"Message - {i + 1}") { ReferenceVersion = version });
                        version = recentActivityAddedResult.Version;
                    }));
        }
        await Task.WhenAll(tasks);
        recentActivityList = await multiProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);
        // this works
        var aggregateRecentActivity
            = await projectionService.AsDefaultStateFromInitialAsync<RecentActivity>(createRecentActivityResult.AggregateId!.Value);
        var aggregateRecentActivity2 = await projectionService.AsDefaultStateAsync<RecentActivity>(createRecentActivityResult.AggregateId!.Value);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + 1, aggregateRecentActivity!.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);

        var snapshotManager = await projectionService.AsDefaultStateFromInitialAsync<SnapshotManager>(SnapshotManager.SharedId);
        _testOutputHelper.WriteLine("-requests-");
        if (snapshotManager is not null)
        {
            foreach (var key in snapshotManager.Payload.Requests)
            {
                _testOutputHelper.WriteLine(key);
            }
            _testOutputHelper.WriteLine("-request takens-");
            foreach (var key in snapshotManager.Payload.RequestTakens)
            {
                _testOutputHelper.WriteLine(key);
            }
        }
    }

    private async Task CheckSnapshots<TAggregatePayload>(List<SnapshotDocument> snapshots, Guid aggregateId)
        where TAggregatePayload : IAggregatePayload, new()
    {
        foreach (var state in snapshots.Select(snapshot => snapshot.GetState()))
        {
            if (state is null)
            {
                throw new SekibanInvalidArgumentException();
            }
            var fromInitial = await projectionService.AsDefaultStateFromInitialAsync<TAggregatePayload>(aggregateId, toVersion: state.Version);
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
        var createRecentActivityResult = await commandExecutor.ExecCommandAsync(new CreateRecentInMemoryActivity());

        var recentActivityList = await multiProjectionService.GetAggregateList<RecentInMemoryActivity>();
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
                        var recentActivityAddedResult = await commandExecutor.ExecCommandAsync(
                            new AddRecentInMemoryActivity(createRecentActivityResult!.AggregateId!.Value, $"Message - {i + 1}")
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
            = await projectionService.AsDefaultStateFromInitialAsync<RecentInMemoryActivity>(createRecentActivityResult.AggregateId!.Value);
        var aggregateRecentActivity2
            = await projectionService.AsDefaultStateAsync<RecentInMemoryActivity>(createRecentActivityResult.AggregateId!.Value);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + 1, aggregateRecentActivity!.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);
    }
}
