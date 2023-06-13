using FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes;
using Microsoft.Extensions.Caching.Memory;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Cache;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Query.SingleProjections.Projections;
using Sekiban.Infrastructure.Cosmos;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories;

public class InheritedSubtypeTests : TestBase
{
    private readonly CosmosDbFactory _cosmosDbFactory;
    private readonly IDocumentPersistentWriter _documentPersistentWriter;
    private readonly HybridStoreManager _hybridStoreManager;
    private readonly InMemoryDocumentStore _inMemoryDocumentStore;
    private readonly IMemoryCacheAccessor _memoryCache;
    private readonly IAggregateLoader aggregateLoader;
    private readonly ICommandExecutor commandExecutor;
    private readonly ISingleProjectionSnapshotAccessor singleProjectionSnapshotAccessor;
    private CommandExecutorResponseWithEvents commandResponse = default!;

    public InheritedSubtypeTests(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper,
        new CosmosSekibanServiceProviderGenerator())
    {
        _cosmosDbFactory = GetService<CosmosDbFactory>();
        commandExecutor = GetService<ICommandExecutor>();
        aggregateLoader = GetService<IAggregateLoader>();
        GetService<IMultiProjectionService>();
        _hybridStoreManager = GetService<HybridStoreManager>();
        _inMemoryDocumentStore = GetService<InMemoryDocumentStore>();
        _memoryCache = GetService<IMemoryCacheAccessor>();
        GetService<IDocumentPersistentRepository>();
        singleProjectionSnapshotAccessor = GetService<ISingleProjectionSnapshotAccessor>();
        _documentPersistentWriter = GetService<IDocumentPersistentWriter>();

    }

    [Fact]
    public async Task SubtypeSnapshotTest1UseJson()
    {
        // 先に全データを削除する
        await _cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Default);
        await _cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.Command, AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.Command);



        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(new OpenInheritedAggregate { YearMonth = 202001 });
        Assert.NotNull(commandResponse.AggregateId);
        var aggregateId = commandResponse.AggregateId!.Value;

        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new CloseInheritedAggregate { Reason = "test", AggregateId = aggregateId });


        var aggregateState = await aggregateLoader.AsDefaultStateFromInitialAsync<IInheritedAggregate>(aggregateId);
        var snapshotDocument = await singleProjectionSnapshotAccessor.SnapshotDocumentFromAggregateStateAsync(aggregateState!);
        await _documentPersistentWriter.SaveSingleSnapshotAsync(snapshotDocument!, typeof(IInheritedAggregate), false);

        _inMemoryDocumentStore.ResetInMemoryStore();
        _hybridStoreManager.ClearHybridPartitions();
        ((MemoryCache)_memoryCache.Cache).Compact(1);

        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new ReopenInheritedAggregate { Reason = "reopen", AggregateId = aggregateId });

        _inMemoryDocumentStore.ResetInMemoryStore();
        _hybridStoreManager.ClearHybridPartitions();
        ((MemoryCache)_memoryCache.Cache).Compact(1);

        aggregateState = await aggregateLoader.AsDefaultStateAsync<IInheritedAggregate>(aggregateId);

        Assert.Equal(3, aggregateState!.Version);
        Assert.Equal(2, aggregateState.AppliedSnapshotVersion);
        Assert.True(aggregateState.Payload is ProcessingSubAggregate);
    }
    [Fact]
    public async Task SubtypeSnapshotTest2UseBlob()
    {
        // 先に全データを削除する
        await _cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Default);
        await _cosmosDbFactory.DeleteAllFromEventContainer(AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.Command, AggregateContainerGroup.Dissolvable);
        await _cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.Command);



        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(new OpenInheritedAggregate { YearMonth = 202001 });
        Assert.NotNull(commandResponse.AggregateId);
        var aggregateId = commandResponse.AggregateId!.Value;

        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new CloseInheritedAggregate { Reason = "test", AggregateId = aggregateId });


        var aggregateState = await aggregateLoader.AsDefaultStateFromInitialAsync<IInheritedAggregate>(aggregateId);
        var snapshotDocument = await singleProjectionSnapshotAccessor.SnapshotDocumentFromAggregateStateAsync(aggregateState!);
        await _documentPersistentWriter.SaveSingleSnapshotAsync(snapshotDocument!, typeof(IInheritedAggregate), true);

        _inMemoryDocumentStore.ResetInMemoryStore();
        _hybridStoreManager.ClearHybridPartitions();
        ((MemoryCache)_memoryCache.Cache).Compact(1);

        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new ReopenInheritedAggregate { Reason = "reopen", AggregateId = aggregateId });

        _inMemoryDocumentStore.ResetInMemoryStore();
        _hybridStoreManager.ClearHybridPartitions();
        ((MemoryCache)_memoryCache.Cache).Compact(1);

        aggregateState = await aggregateLoader.AsDefaultStateAsync<IInheritedAggregate>(aggregateId);

        Assert.Equal(3, aggregateState!.Version);
        Assert.Equal(2, aggregateState.AppliedSnapshotVersion);
        Assert.True(aggregateState.Payload is ProcessingSubAggregate);
    }
}
