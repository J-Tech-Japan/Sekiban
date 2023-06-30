using FeatureCheck.Domain.Aggregates.SubTypes.InheritedSubtypes;
using FeatureCheck.Domain.Shared;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.SingleProjections.Projections;
using Sekiban.Infrastructure.Cosmos;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories;

public class InheritedSubtypeTests : TestBase<FeatureCheckDependency>
{
    private readonly CosmosDbFactory _cosmosDbFactory;
    private readonly ISingleProjectionSnapshotAccessor singleProjectionSnapshotAccessor;
    private CommandExecutorResponseWithEvents commandResponse = default!;

    public InheritedSubtypeTests(SekibanTestFixture sekibanTestFixture, ITestOutputHelper testOutputHelper) : base(
        sekibanTestFixture,
        testOutputHelper,
        new CosmosSekibanServiceProviderGenerator())
    {
        _cosmosDbFactory = GetService<CosmosDbFactory>();
        GetService<IMultiProjectionService>();
        GetService<IDocumentPersistentRepository>();
        singleProjectionSnapshotAccessor = GetService<ISingleProjectionSnapshotAccessor>();
    }

    [Fact]
    public async Task SubtypeSnapshotTest1UseJson()
    {
        RemoveAllFromDefaultAndDissolvable();

        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(new OpenInheritedAggregate { YearMonth = 202001 });
        Assert.NotNull(commandResponse.AggregateId);
        var aggregateId = commandResponse.AggregateId!.Value;

        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new CloseInheritedAggregate { Reason = "test", AggregateId = aggregateId });


        var aggregateState = await aggregateLoader.AsDefaultStateFromInitialAsync<IInheritedAggregate>(aggregateId);
        var snapshotDocument = await singleProjectionSnapshotAccessor.SnapshotDocumentFromAggregateStateAsync(aggregateState!);
        await documentPersistentWriter.SaveSingleSnapshotAsync(snapshotDocument!, typeof(IInheritedAggregate), false);

        ResetInMemoryDocumentStoreAndCache();

        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new ReopenInheritedAggregate { Reason = "reopen", AggregateId = aggregateId });

        ResetInMemoryDocumentStoreAndCache();

        aggregateState = await aggregateLoader.AsDefaultStateAsync<IInheritedAggregate>(aggregateId);

        Assert.Equal(3, aggregateState!.Version);
        Assert.Equal(2, aggregateState.AppliedSnapshotVersion);
        Assert.True(aggregateState.Payload is ProcessingSubAggregate);
    }
    [Fact]
    public async Task SubtypeSnapshotTest2UseBlob()
    {
        RemoveAllFromDefaultAndDissolvable();

        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(new OpenInheritedAggregate { YearMonth = 202001 });
        Assert.NotNull(commandResponse.AggregateId);
        var aggregateId = commandResponse.AggregateId!.Value;

        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new CloseInheritedAggregate { Reason = "test", AggregateId = aggregateId });


        var aggregateState = await aggregateLoader.AsDefaultStateFromInitialAsync<IInheritedAggregate>(aggregateId);
        var snapshotDocument = await singleProjectionSnapshotAccessor.SnapshotDocumentFromAggregateStateAsync(aggregateState!);
        await documentPersistentWriter.SaveSingleSnapshotAsync(snapshotDocument!, typeof(IInheritedAggregate), true);

        ResetInMemoryDocumentStoreAndCache();

        commandResponse = await commandExecutor.ExecCommandWithEventsAsync(
            new ReopenInheritedAggregate { Reason = "reopen", AggregateId = aggregateId });

        ResetInMemoryDocumentStoreAndCache();

        aggregateState = await aggregateLoader.AsDefaultStateAsync<IInheritedAggregate>(aggregateId);

        Assert.Equal(3, aggregateState!.Version);
        Assert.Equal(2, aggregateState.AppliedSnapshotVersion);
        Assert.True(aggregateState.Payload is ProcessingSubAggregate);
    }
}
