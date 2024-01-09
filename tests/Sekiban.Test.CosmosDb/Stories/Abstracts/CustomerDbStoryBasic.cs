using FeatureCheck.Domain.Aggregates.ALotOfEvents;
using FeatureCheck.Domain.Aggregates.ALotOfEvents.Commands;
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
using FeatureCheck.Domain.Shared;
using FeatureCheck.Domain.Shared.Exceptions;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Query.SingleProjections.Projections;
using Sekiban.Core.Setting;
using Sekiban.Core.Snapshot;
using Sekiban.Core.Snapshot.Aggregate;
using Sekiban.Core.Types;
using Sekiban.Testing.Shared;
using Sekiban.Testing.Story;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories.Abstracts;

public abstract class CustomerDbStoryBasic : TestBase<FeatureCheckDependency>
{
    private readonly IBlobAccessor blobAccessor;
    private readonly ISingleProjectionSnapshotAccessor singleProjectionSnapshotAccessor;
    public CustomerDbStoryBasic(
        SekibanTestFixture sekibanTestFixture,
        ITestOutputHelper testOutputHelper,
        ISekibanServiceProviderGenerator providerGenerator) : base(sekibanTestFixture, testOutputHelper, providerGenerator)
    {
        singleProjectionSnapshotAccessor = GetService<ISingleProjectionSnapshotAccessor>();
        blobAccessor = GetService<IBlobAccessor>();
    }

    [Fact]
    public async Task DocumentDbStory()
    {
        RemoveAllFromDefaultAndDissolvable();

        // create list branch
        var branchList = await multiProjectionService.GetAggregateList<Branch>();
        Assert.Empty(branchList);
        var branchResult = await commandExecutor.ExecCommandAsync(new CreateBranch("Japan"));
        var branchId = branchResult.AggregateId!.Value;
        Assert.NotNull(branchResult);
        Assert.NotNull(branchResult.AggregateId);
        branchList = await multiProjectionService.GetAggregateList<Branch>();
        Assert.Single(branchList);
        var branchFromList = branchList.First(m => m.AggregateId == branchId);
        Assert.NotNull(branchFromList);

        await commandExecutor.ExecCommandAsync(new CreateBranch("USA"));
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
        var createClientResult = await commandExecutor.ExecCommandAsync(new CreateClient(branchId, originalName, "tanaka@example.com"));
        var clientId = createClientResult.AggregateId!.Value;
        Assert.NotNull(createClientResult);
        Assert.NotNull(createClientResult.AggregateId);
        clientList = await multiProjectionService.GetAggregateList<Client>();
        Assert.Single(clientList);

        // singleAggregateProjection
        clientNameList = await multiProjectionService.GetSingleProjectionList<ClientNameHistoryProjection>();
        Assert.Single(clientNameList);
        var tanakaProjection = clientNameList.First(m => m.AggregateId == clientId);
        Assert.Single(tanakaProjection.Payload.ClientNames);
        Assert.Equal(originalName, tanakaProjection.Payload.ClientNames.First().Name);

        var clientNameListFromMultiple = await multiProjectionService.GetSingleProjectionList<ClientNameHistoryProjection>();
        Assert.Single(clientNameListFromMultiple);
        Assert.Equal(clientNameList.First().AggregateId, clientNameListFromMultiple.First().AggregateId);

        await commandExecutor.ExecCommandAsync(new CreateBranch("California"));
        var _ = await multiProjectionService.GetAggregateList<Branch>();

        var secondName = "田中 太郎";
        // should throw version error 
        await Assert.ThrowsAsync<SekibanCommandInconsistentVersionException>(
            async () =>
            {
                await commandExecutor.ExecCommandAsync(new ChangeClientName(clientId, secondName));
            });
        // change name
        var changeNameResult = await commandExecutor.ExecCommandAsync(
            new ChangeClientName(clientId, secondName) { ReferenceVersion = createClientResult.Version });

        // change name projection
        clientNameList = await multiProjectionService.GetSingleProjectionList<ClientNameHistoryProjection>();
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
        var changeNameProjection = await aggregateLoader.AsSingleProjectionStateAsync<ClientNameHistoryProjection>(clientId);
        Assert.NotNull(changeNameProjection);

        // loyalty point should be created with event subscribe
        var __ = await multiProjectionService.GetAggregateList<LoyaltyPoint>();
        Assert.Single(clientList);
        // first point = 0
        var loyaltyPoint = await aggregateLoader.AsDefaultStateAsync<LoyaltyPoint>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(0, loyaltyPoint.Payload.CurrentPoint);

        var datetimeFirst = DateTime.Now;
        var addPointResult = await commandExecutor.ExecCommandAsync(
            new AddLoyaltyPoint(clientId, datetimeFirst, LoyaltyPointReceiveTypeKeys.FlightDomestic, 1000, "")
            {
                ReferenceVersion = loyaltyPoint.Version
            });
        Assert.NotNull(addPointResult);
        Assert.NotNull(addPointResult.AggregateId);

        loyaltyPoint = await aggregateLoader.AsDefaultStateAsync<LoyaltyPoint>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(1000, loyaltyPoint.Payload.CurrentPoint);

        // should throw not enough point error 
        await Assert.ThrowsAsync<SekibanLoyaltyPointNotEnoughException>(
            async () =>
            {
                await commandExecutor.ExecCommandAsync(
                    new UseLoyaltyPoint(clientId, datetimeFirst.AddSeconds(1), LoyaltyPointUsageTypeKeys.FlightUpgrade, 2000, "")
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

        loyaltyPoint = await aggregateLoader.AsDefaultStateAsync<LoyaltyPoint>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(800, loyaltyPoint.Payload.CurrentPoint);

        var p = await multiProjectionService.GetMultiProjectionAsync<ClientLoyaltyPointMultiProjection>();
        Assert.NotNull(p);
        Assert.Equal(3, p.Payload.Branches.Count);
        Assert.Single(p.Payload.Records);

        // delete client
        var deleteClientResult = await commandExecutor.ExecCommandAsync(new DeleteClient(clientId) { ReferenceVersion = versionCN });
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

        var recentActivityList = await multiProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);
        var version = createRecentActivityResult.Version;
        var count = 160;
        foreach (var i in Enumerable.Range(0, count))
        {
            var recentActivityAddedResult = await commandExecutor.ExecCommandAsync(
                new AddRecentActivity(createRecentActivityResult.AggregateId!.Value, $"Message - {i + 1}") { ReferenceVersion = version });
            version = recentActivityAddedResult.Version;
        }

        recentActivityList = await multiProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);
        Assert.Equal(count + 1, version);

        // only publish event run

        var recentActivityId = createRecentActivityResult.AggregateId!.Value;

        await commandExecutor.ExecCommandAsync(new OnlyPublishingAddRecentActivity(recentActivityId, "only publish event"));

        // get single aggregate and applied event
        var recentActivityState = await aggregateLoader.AsDefaultStateAsync<RecentActivity>(recentActivityId);
        Assert.Equal("only publish event", recentActivityState?.Payload.LatestActivities.First().Activity);

        p = await multiProjectionService.GetMultiProjectionAsync<ClientLoyaltyPointMultiProjection>();
        Assert.NotNull(p);
        Assert.Equal(3, p.Payload.Branches.Count);
        Assert.Empty(p.Payload.Records);
        var snapshotManager = await aggregateLoader.AsDefaultStateFromInitialAsync<SnapshotManager>(SnapshotManager.SharedId);
        if (snapshotManager is null)
        {
            TestOutputHelper.WriteLine("snapshot manager is null");
        } else
        {
            TestOutputHelper.WriteLine("-requests-");
            foreach (var key in snapshotManager.Payload.Requests)
            {
                TestOutputHelper.WriteLine(key);
            }
            TestOutputHelper.WriteLine("-request takens-");
            foreach (var key in snapshotManager.Payload.RequestTakens)
            {
                TestOutputHelper.WriteLine(key);
            }
        }

        branchList = await multiProjectionService.GetAggregateList<Branch>();
        Assert.Equal(3, branchList.Count);
    }


    [Fact]
    public async Task DocumentDbStoryLoyaltyPointThrows()
    {
        RemoveAllFromDefaultAndDissolvable();

        // create list branch
        var branchList = await multiProjectionService.GetAggregateList<Branch>();
        Assert.Empty(branchList);
        var branchResult = await commandExecutor.ExecCommandAsync(new CreateBranch("Japan"));
        var branchId = branchResult.AggregateId!.Value;
        // create client
        var originalName = "Tanaka Taro";
        var createClientResult = await commandExecutor.ExecCommandAsync(new CreateClient(branchId, originalName, "tanaka@example.com"));
        var clientId = createClientResult.AggregateId!.Value;

        var loyaltyPoint = await aggregateLoader.AsDefaultStateAsync<LoyaltyPoint>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(0, loyaltyPoint.Payload.CurrentPoint);

        var datetimeFirst = DateTime.Now;
        var addPointResult = await commandExecutor.ExecCommandAsync(
            new AddLoyaltyPoint(clientId, datetimeFirst, LoyaltyPointReceiveTypeKeys.FlightDomestic, 1000, "")
            {
                ReferenceVersion = loyaltyPoint.Version
            });
        Assert.NotNull(addPointResult);
        Assert.NotNull(addPointResult.AggregateId);

        loyaltyPoint = await aggregateLoader.AsDefaultStateAsync<LoyaltyPoint>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(1000, loyaltyPoint.Payload.CurrentPoint);

        // should throw not enough point error 
        await Assert.ThrowsAsync<SekibanLoyaltyPointNotEnoughException>(
            async () =>
            {
                await commandExecutor.ExecCommandAsync(
                    new UseLoyaltyPoint(clientId, datetimeFirst.AddSeconds(1), LoyaltyPointUsageTypeKeys.FlightUpgrade, 2000, "")
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

        loyaltyPoint = await aggregateLoader.AsDefaultStateAsync<LoyaltyPoint>(clientId);
        Assert.NotNull(loyaltyPoint);
        Assert.Equal(800, loyaltyPoint.Payload.CurrentPoint);

        var p = await multiProjectionService.GetMultiProjectionAsync<ClientLoyaltyPointMultiProjection>();
        Assert.NotNull(p);
        Assert.Single(p.Payload.Branches);
        Assert.Single(p.Payload.Records);

        // delete client
        var deleteClientResult = await commandExecutor.ExecCommandAsync(new DeleteClient(clientId) { ReferenceVersion = createClientResult.Version });
        Assert.NotNull(deleteClientResult);
        Assert.NotNull(deleteClientResult.AggregateId);
        // client deleted
        var clientList = await multiProjectionService.GetAggregateList<Client>();
        Assert.Empty(clientList);
        // can find deleted client
        clientList = await multiProjectionService.GetAggregateList<Client>(QueryListType.DeletedOnly);
        Assert.Single(clientList);
        clientList = await multiProjectionService.GetAggregateList<Client>(QueryListType.ActiveAndDeleted);
        Assert.Single(clientList);

        // loyalty point should be created with event subscribe
        var loyaltyPointList = await multiProjectionService.GetAggregateList<LoyaltyPoint>();
        Assert.Empty(loyaltyPointList);
        loyaltyPointList = await multiProjectionService.GetAggregateList<LoyaltyPoint>(QueryListType.DeletedOnly);
        Assert.Single(loyaltyPointList);
    }



    [Fact]
    public async Task SnapshotTestAsync()
    {
        RemoveAllFromDefaultAndDissolvable();
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

        ResetInMemoryDocumentStoreAndCache();

        var client = await aggregateLoader.AsDefaultStateAsync<Client>(clientResult.AggregateId!.Value);
        Assert.NotNull(client);
        var clientProjection = await aggregateLoader.AsSingleProjectionStateAsync<ClientNameHistoryProjection>(clientResult.AggregateId!.Value);
        Assert.NotNull(clientProjection);
    }
    [Fact]
    public void CheckBlobAccessorBlobConnectionString()
    {
        var connectionString = blobAccessor.BlobConnectionString();
        TestOutputHelper.WriteLine("BlobConnectionString: " + connectionString);
        Assert.NotNull(connectionString);
    }
    [Fact]
    public async Task ManualSnapshotTestAsync()
    {
        RemoveAllFromDefaultAndDissolvable();
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

        var aggregateId = clientResult.AggregateId!.Value;

        var client1 = await aggregateLoader.AsDefaultStateFromInitialAsync<Client>(aggregateId, toVersion: 80);
        var clientSnapshot = await singleProjectionSnapshotAccessor.SnapshotDocumentFromAggregateStateAsync(client1!);
        await documentPersistentWriter.SaveSingleSnapshotAsync(clientSnapshot!, typeof(Client), false);
        var client2 = await aggregateLoader.AsDefaultStateFromInitialAsync<Client>(aggregateId);
        var clientSnapshot2 = await singleProjectionSnapshotAccessor.SnapshotDocumentFromAggregateStateAsync(client2!);
        await documentPersistentWriter.SaveSingleSnapshotAsync(clientSnapshot2!, typeof(Client), true);

        var snapshots = await documentPersistentRepository.GetSnapshotsForAggregateAsync(aggregateId, typeof(Client), typeof(Client));

        Assert.Contains(clientSnapshot!.Id, snapshots.Select(m => m.Id));
        var clientFromSnapshot = snapshots.First(m => m.Id == clientSnapshot.Id).GetState();
        Assert.NotNull(clientFromSnapshot);

        Assert.Contains(clientSnapshot2!.Id, snapshots.Select(m => m.Id));
        var clientFromSnapshot2 = snapshots.First(m => m.Id == clientSnapshot2.Id).GetState();
        Assert.NotNull(clientFromSnapshot2);

        var projection1
            = await aggregateLoader.AsSingleProjectionStateFromInitialAsync<ClientNameHistoryProjection>(
                clientResult.AggregateId!.Value,
                toVersion: 80);
        var projectionSnapshot = await singleProjectionSnapshotAccessor.SnapshotDocumentFromSingleProjectionStateAsync(projection1!, typeof(Client));
        await documentPersistentWriter.SaveSingleSnapshotAsync(projectionSnapshot!, typeof(Client), false);
        var projection2 = await aggregateLoader.AsSingleProjectionStateFromInitialAsync<ClientNameHistoryProjection>(clientResult.AggregateId!.Value);
        var projectionSnapshot2 = await singleProjectionSnapshotAccessor.SnapshotDocumentFromSingleProjectionStateAsync(projection2!, typeof(Client));
        await documentPersistentWriter.SaveSingleSnapshotAsync(projectionSnapshot2!, typeof(Client), true);

        var projectionSnapshots = await documentPersistentRepository.GetSnapshotsForAggregateAsync(
            aggregateId,
            typeof(Client),
            typeof(ClientNameHistoryProjection));

        Assert.Contains(projectionSnapshot!.Id, projectionSnapshots.Select(m => m.Id));

        var clientProjectionFromSnapshot = projectionSnapshots.First(m => m.Id == projectionSnapshot.Id).GetState();
        Assert.NotNull(clientProjectionFromSnapshot);

        Assert.Contains(projectionSnapshot2!.Id, projectionSnapshots.Select(m => m.Id));
        var clientProjectionFromSnapshot2 = projectionSnapshots.First(m => m.Id == projectionSnapshot2.Id).GetState();
        Assert.NotNull(clientProjectionFromSnapshot2);

    }


    [Fact]
    public void DeleteOnlyTest() => RemoveAllFromDefaultAndDissolvable();

    [Trait(SekibanTestConstants.Category, SekibanTestConstants.Categories.Flaky)]
    [Fact]
    public void NoFlakyTest()
    {
    }

    [Fact]
    [Trait(SekibanTestConstants.Category, SekibanTestConstants.Categories.Flaky)]
    public async Task AsynchronousExecutionTestAsync()
    {
        RemoveAllFromDefaultAndDissolvable();

        // create recent activity
        var createRecentActivityResult = await commandExecutor.ExecCommandAsync(new CreateRecentActivity());

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
        var projection = await aggregateLoader.AsSingleProjectionStateAsync<TenRecentProjection>(createRecentActivityResult.AggregateId!.Value);
        Assert.NotNull(projection);
        // this works
        var aggregateRecentActivity
            = await aggregateLoader.AsDefaultStateFromInitialAsync<RecentActivity>(createRecentActivityResult.AggregateId!.Value);
        var aggregateRecentActivity2 = await aggregateLoader.AsDefaultStateAsync<RecentActivity>(createRecentActivityResult.AggregateId!.Value);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + 1, aggregateRecentActivity.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);

        var snapshotManager = await aggregateLoader.AsDefaultStateFromInitialAsync<SnapshotManager>(SnapshotManager.SharedId);
        TestOutputHelper.WriteLine("-requests-");
        foreach (var key in snapshotManager!.Payload.Requests)
        {
            TestOutputHelper.WriteLine(key);
        }
        TestOutputHelper.WriteLine("-request takens-");
        foreach (var key in snapshotManager.Payload.RequestTakens)
        {
            TestOutputHelper.WriteLine(key);
        }

        var snapshots = await documentPersistentRepository.GetSnapshotsForAggregateAsync(
            createRecentActivityResult.AggregateId!.Value,
            typeof(RecentActivity),
            typeof(RecentActivity));

        await CheckSnapshots<RecentActivity>(snapshots, createRecentActivityResult.AggregateId!.Value);

        var snapshots2 = await documentPersistentRepository.GetSnapshotsForAggregateAsync(
            createRecentActivityResult.AggregateId!.Value,
            typeof(RecentActivity),
            typeof(TenRecentProjection));

        await CheckProjectionSnapshots<TenRecentProjection>(snapshots2, createRecentActivityResult.AggregateId!.Value);

        await documentPersistentRepository.GetSnapshotsForAggregateAsync(
            createRecentActivityResult.AggregateId!.Value,
            typeof(RecentActivity),
            typeof(TenRecentProjection));

        ResetInMemoryDocumentStoreAndCache();
        await ContinuousExecutionTestAsync();
    }

    [Fact]
    [Trait(SekibanTestConstants.Category, SekibanTestConstants.Categories.Flaky)]
    public async Task AsynchronousExecutionConsistencyTestAsync()
    {
        RemoveAllFromDefaultAndDissolvable();

        // create recent activity
        var createRecentActivityResult = await commandExecutor.ExecCommandAsync(new CreateRecentActivity());
        var aggregateId = createRecentActivityResult.AggregateId!.Value;
        var recentActivityList = await multiProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);
        var version = createRecentActivityResult.Version;
        var tasks = new List<Task>();
        var count = 50;
        foreach (var i in Enumerable.Range(0, count))
        {
            tasks.Add(
                Task.Run(
                    async () =>
                    {
                        var recentActivityAddedResult = await commandExecutor.ExecCommandAsync(
                            new AddRecentActivity(aggregateId, $"Message - {i + 1}") { ReferenceVersion = version });
                        version = recentActivityAddedResult.Version;
                    }));
        }
        await Task.WhenAll(tasks);

        var events = (await aggregateLoader.AllEventsAsync<RecentActivity>(aggregateId) ?? Enumerable.Empty<IEvent>()).ToList();
        var versionShouldBe = 1;
        foreach (var ev in events)
        {
            Assert.Equal(versionShouldBe, ev.Version);
            versionShouldBe++;
        }
    }



    private async Task CheckSnapshots<TAggregatePayload>(List<SnapshotDocument> snapshots, Guid aggregateId)
        where TAggregatePayload : IAggregatePayloadCommon
    {
        TestOutputHelper.WriteLine($"snapshots {typeof(TAggregatePayload).Name} {snapshots.Count} ");
        foreach (var snapshot in snapshots)
        {
            TestOutputHelper.WriteLine($"snapshot {snapshot.AggregateType}  {snapshot.Id}  {snapshot.SavedVersion} is checking");
            var state = snapshot.GetState();
            if (state is null)
            {
                TestOutputHelper.WriteLine($"Snapshot {snapshot.AggregateType} {snapshot.Id} {snapshot.SavedVersion}  is null");
                throw new SekibanInvalidArgumentException($"Snapshot {snapshot.AggregateType} {snapshot.SavedVersion}  is null");
            }
            TestOutputHelper.WriteLine($"Snapshot {snapshot.AggregateType}  {snapshot.Id}  {snapshot.SavedVersion}  is not null");
            var fromInitial = await aggregateLoader.AsDefaultStateFromInitialAsync<TAggregatePayload>(aggregateId, toVersion: state.Version) ?? throw new SekibanInvalidArgumentException();
            Assert.Equal(fromInitial.Version, state.Version);
            Assert.Equal(fromInitial.LastEventId, state.LastEventId);
        }
    }

    private async Task CheckProjectionSnapshots<TAggregatePayload>(List<SnapshotDocument> snapshots, Guid aggregateId)
        where TAggregatePayload : class, ISingleProjectionPayloadCommon
    {
        typeof(TAggregatePayload).GetAggregatePayloadTypeFromSingleProjectionPayload();
        TestOutputHelper.WriteLine($"snapshots {typeof(TAggregatePayload).Name} {snapshots.Count} ");
        foreach (var snapshot in snapshots)
        {
            TestOutputHelper.WriteLine(
                $"snapshot {snapshot.AggregateType} {snapshot.DocumentTypeName} {snapshot.Id}  {snapshot.SavedVersion} is checking");
            var state = snapshot.GetState();
            if (state is null)
            {
                TestOutputHelper.WriteLine(
                    $"Snapshot {snapshot.AggregateType} {snapshot.DocumentTypeName} {snapshot.Id} {snapshot.SavedVersion}  is null");
                throw new SekibanInvalidArgumentException($"Snapshot {snapshot.AggregateType} {snapshot.SavedVersion}  is null");
            }
            TestOutputHelper.WriteLine(
                $"Snapshot {snapshot.AggregateType} {snapshot.DocumentTypeName} {snapshot.Id}  {snapshot.SavedVersion}  is not null");
            var fromInitial = await aggregateLoader.AsSingleProjectionStateFromInitialAsync<TAggregatePayload>(aggregateId, toVersion: state.Version) ?? throw new SekibanInvalidArgumentException();
            Assert.Equal(fromInitial.Version, state.Version);
            Assert.Equal(fromInitial.LastEventId, state.LastEventId);
        }
    }

    [Fact]
    public async Task AsynchronousInMemoryExecutionTestAsync()
    {
        // create recent activity
        var createRecentActivityResult = await commandExecutor.ExecCommandAsync(new CreateRecentInMemoryActivity());

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
                        var recentActivityAddedResult = await commandExecutor.ExecCommandAsync(
                            new AddRecentInMemoryActivity(createRecentActivityResult.AggregateId!.Value, $"Message - {i + 1}")
                            {
                                ReferenceVersion = version
                            });
                        version = recentActivityAddedResult.Version;
                        TestOutputHelper.WriteLine($"{i} - {recentActivityAddedResult.Version}");
                    }));
        }
        await Task.WhenAll(tasks);
        recentActivityList = await multiProjectionService.GetAggregateList<RecentInMemoryActivity>();
        Assert.Single(recentActivityList);

        var aggregateRecentActivity
            = await aggregateLoader.AsDefaultStateFromInitialAsync<RecentInMemoryActivity>(createRecentActivityResult.AggregateId!.Value);
        var aggregateRecentActivity2
            = await aggregateLoader.AsDefaultStateAsync<RecentInMemoryActivity>(createRecentActivityResult.AggregateId!.Value);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + 1, aggregateRecentActivity.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);
    }

    private async Task ContinuousExecutionTestAsync()
    {
        TestOutputHelper.WriteLine("481");
        var recentActivityList = await multiProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);
        var aggregateId = recentActivityList.First().AggregateId;

        var aggregate = await aggregateLoader.AsAggregateAsync<RecentActivity>(aggregateId);
        Assert.NotNull(aggregate);
        var _ = await aggregateLoader.AsDefaultStateAsync<RecentActivity>(aggregateId);

        //var aggregateRecentActivity =
        //    await projectionService
        //        .AsSingleProjectionStateFromInitialAsync<CreateRecentActivity>(
        //            aggregateId);
        //Assert.Single(recentActivityList);
        //Assert.NotNull(aggregateRecentActivity);
        //Assert.NotNull(aggregateRecentActivity2);
        //Assert.Equal(aggregateRecentActivity!.Version, aggregateRecentActivity2!.Version);

        TestOutputHelper.WriteLine("498");
        var version = recentActivityList.First().Version;
        var tasks = new List<Task>();
        var count = 180;
        foreach (var i in Enumerable.Range(0, count))
        {
            tasks.Add(
                Task.Run(
                    async () =>
                    {
                        var recentActivityAddedResult = await commandExecutor.ExecCommandAsync(
                            new AddRecentActivity(aggregateId, $"Message - {i + 1}") { ReferenceVersion = version });
                        version = recentActivityAddedResult.Version;
                    }));
        }
        await Task.WhenAll(tasks);
        recentActivityList = await multiProjectionService.GetAggregateList<RecentActivity>();
        Assert.Single(recentActivityList);

        TestOutputHelper.WriteLine("518");

        var snapshots = await documentPersistentRepository.GetSnapshotsForAggregateAsync(aggregateId, typeof(RecentActivity), typeof(RecentActivity));
        await CheckSnapshots<RecentActivity>(snapshots, aggregateId);
        var projectionSnapshots = await documentPersistentRepository.GetSnapshotsForAggregateAsync(
            aggregateId,
            typeof(RecentActivity),
            typeof(TenRecentProjection));
        Assert.NotEmpty(projectionSnapshots);
        await CheckProjectionSnapshots<TenRecentProjection>(projectionSnapshots, aggregateId);

        // check aggregate result
        var aggregateRecentActivity = await aggregateLoader.AsDefaultStateFromInitialAsync<RecentActivity>(aggregateId);
        var aggregateRecentActivity2 = await aggregateLoader.AsDefaultStateAsync<RecentActivity>(aggregateId);
        Assert.Single(recentActivityList);
        Assert.NotNull(aggregateRecentActivity);
        Assert.Equal(count + aggregate.ToState().Version, aggregateRecentActivity.Version);
        Assert.Equal(aggregateRecentActivity.Version, aggregateRecentActivity2!.Version);

        var snapshotManager = await aggregateLoader.AsDefaultStateFromInitialAsync<SnapshotManager>(SnapshotManager.SharedId);
        TestOutputHelper.WriteLine("-requests-");
        foreach (var key in snapshotManager!.Payload.Requests)
        {
            TestOutputHelper.WriteLine(key);
        }
        TestOutputHelper.WriteLine("-request takens-");
        foreach (var key in snapshotManager.Payload.RequestTakens)
        {
            TestOutputHelper.WriteLine(key);
        }
    }

    [Fact]
    public async Task ALotOfEventCreateTest()
    {
        var eventCount = 300;
        RemoveAllFromDefaultAndDissolvable();
        var result = await commandExecutor.ExecCommandWithEventsAsync(
            new ALotOfEventsCreateCommand { AggregateId = Guid.NewGuid(), NumberOfEvents = eventCount });
        Assert.Equal(eventCount, result.Version);
        Assert.Equal(eventCount, result.Events.Count);
        var aggregate = await aggregateLoader.AsDefaultStateAsync<ALotOfEventsAggregate>(result.AggregateId!.Value);
        Assert.Equal(eventCount, aggregate?.Payload.Count);
    }

    [Fact]
    public async Task CommandWithNoEventsWorksFine()
    {
        RemoveAllFromDefault();
        var result = await commandExecutor.ExecCommandWithEventsAsync(new CreateBranch("JAPAN"));
        var branchId = result.AggregateId!.Value;
        result = await commandExecutor.ExecCommandWithEventsAsync(new CreateClient(branchId, "Test Name", "test@example.com"));
        var clientId = result.AggregateId!.Value;
        result = await commandExecutor.ExecCommandWithEventsAsync(new ClientNoEventsCommand { ClientId = clientId });
        Assert.NotNull(result.AggregateId);
        Assert.Equal(0, result.EventCount);

        var result2 = await commandExecutor.ExecCommandWithEventsAsync(new ClientNoEventsCommand { ClientId = clientId });
        Assert.NotNull(result2.AggregateId);
        Assert.Equal(0, result2.EventCount);

        var result3 = await commandExecutor.ExecCommandWithoutValidationAsync(new ClientNoEventsCommand { ClientId = clientId });
        Assert.NotNull(result3.AggregateId);
        Assert.Equal(0, result3.EventCount);

        var result4 = await commandExecutor.ExecCommandWithoutValidationWithEventsAsync(new ClientNoEventsCommand { ClientId = clientId });
        Assert.NotNull(result4.AggregateId);
        Assert.Equal(0, result4.EventCount);
    }

    [Fact]
    public async Task NoBlockingEventSubscriberWorks()
    {
        RemoveAllFromDefault();
        var result = await commandExecutor.ExecCommandWithEventsAsync(new CreateBranch("JAPAN"));
        var branchId = result.AggregateId!.Value;
        await commandExecutor.ExecCommandWithEventsAsync(new CreateClientWithBranchSubscriber(branchId, "Test Name", "test@example.com"));
        var branch = await aggregateLoader.AsDefaultStateAsync<Branch>(branchId);
        Assert.NotNull(branch);
        Assert.Equal(0, branch.Payload.NumberOfMembers);
        await Task.Delay(2000);
        branch = await aggregateLoader.AsDefaultStateAsync<Branch>(branchId);
        Assert.NotNull(branch);
        Assert.Equal(1, branch.Payload.NumberOfMembers);
    }
    [Fact]
    public async Task CheckNewAggregate()
    {
        RemoveAllFromDefault();
        var targetAggregateId = Guid.NewGuid();
        var result = await aggregateLoader.AsAggregateAsync<Branch>(targetAggregateId) ?? new Aggregate<Branch> { AggregateId = targetAggregateId };
        Assert.Equal(0, result.Version);
        Assert.True(result.IsNew);
        Assert.True(result.ToState().IsNew);
    }
    [Fact]
    public async Task CheckNoEventCanGetAggregateId()
    {
        var response = await commandExecutor.ExecCommandAsync(new NotAddingAnyEventCommand());
        Assert.NotNull(response.AggregateId);
    }
}
