using System.Reflection;
using Amazon.DynamoDBv2;
using Dcb.Domain;
using Microsoft.Extensions.Options;
using Orleans;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.DynamoDB;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.TestSupport;
using Sekiban.Dcb.Testing;
using Sekiban.Dcb.Tests.Cosmos;
using Sekiban.Dcb.Tags;
using Xunit;
using CoreInMemoryEventStore = Sekiban.Dcb.Testing.InMemoryEventStore;

namespace Sekiban.Dcb.Tests.ConditionalAppend;

/// <summary>
///     AC6 invocation proof, not merely structural assignability. Each row wraps the named real unsupported provider
///     with a transparent write counter, then drives every newly-added WithResult command/V2/wire route and the Orleans
///     V2 forwarding route. Dropping the condition at any facade would invoke the handler or provider writer and fail.
/// </summary>
public sealed class ExpectedTagPositionUnsupportedProviderInvocationTests
{
    private const string ServiceId = DefaultServiceIdProvider.DefaultServiceId;
    private const string Tag = "ExpectedMarker:m";

    public static IEnumerable<object[]> UnsupportedProviderFactories()
    {
        yield return ["InMemory", (Func<DcbDomainTypes, IEventStore>)(domain => new CoreInMemoryEventStore(domain.EventTypes))];
        yield return ["SQLite", (Func<DcbDomainTypes, IEventStore>)(domain =>
            new SqliteEventStore(
                Path.Combine(Path.GetTempPath(), $"g40-ac6-{Guid.NewGuid():N}.db"),
                domain.EventTypes,
                new SqliteEventStoreOptions { AutoCreateDatabase = false }))];
        yield return ["Cosmos", (Func<DcbDomainTypes, IEventStore>)(domain => NewCosmosStore(domain))];
        yield return ["Dynamo", (Func<DcbDomainTypes, IEventStore>)(domain => NewDynamoStore(domain))];
    }

    [Theory]
    [MemberData(nameof(UnsupportedProviderFactories))]
    public async Task EveryWithResultAndOrleansExpectedPositionEntry_RejectsNamedProviderBeforeHandlerOrWrite(
        string providerName,
        Func<DcbDomainTypes, IEventStore> createProvider)
    {
        var domain = BuildDomain();
        var observed = new ProviderWriteCountingEventStore(createProvider(domain), providerName);
        var descriptor = observed.DescribeWriteConditions();
        Assert.False(descriptor.Supports(WriteConditionKind.ExpectedTagPosition));

        var executor = new GeneralSekibanExecutor(observed, new InMemoryObjectAccessor(observed, domain), domain);
        var conditional = Assert.IsAssignableFrom<IConditionalCommandExecutor>(executor);
        var handlerCalls = 0;
        var typed = await conditional.ExecuteAsync(
            new ExpectedMarkerCommand(),
            (ExpectedMarkerCommand _, ICommandContext context) =>
            {
                Interlocked.Increment(ref handlerCalls);
                return context.AppendEvent(new ExpectedMarkerEvent("typed"), new ExpectedMarkerTag("m"));
            },
            ExpectedOptions());
        AssertUnsupported(typed);
        Assert.Equal(0, handlerCalls);
        Assert.Equal(0, observed.ProviderWriteCalls);

        // The direct V2 facade is distinct from the JSON acceptor route.
        var directV2 = await Assert.IsAssignableFrom<ISerializedExpectedTagPositionSekibanDcbExecutor>(executor)
            .CommitSerializableEventsWithExpectedTagPositionsAsync(ExpectedV2Request());
        AssertUnsupported(directV2);
        Assert.Equal(0, observed.ProviderWriteCalls);

        var wire = await new SerializedCommitAcceptor(executor).AcceptAsync(ExpectedV2Wire());
        AssertUnsupported(wire);
        Assert.Equal(0, observed.ProviderWriteCalls);

        // Orleans owns a forwarding body; this verifies its call reaches the same fail-closed gate before it ever asks a
        // grain or writes the provider. The wire call also exercises the acceptor's optional-executor feature detection.
        var cluster = DispatchProxy.Create<IClusterClient, UnreachableClusterClient>();
        var orleans = new Sekiban.Dcb.Orleans.OrleansDcbExecutor(cluster, observed, domain);
        var orleansDirectV2 = await Assert.IsAssignableFrom<ISerializedExpectedTagPositionSekibanDcbExecutor>(orleans)
            .CommitSerializableEventsWithExpectedTagPositionsAsync(ExpectedV2Request());
        AssertUnsupported(orleansDirectV2);
        Assert.Equal(0, observed.ProviderWriteCalls);

        var orleansWire = await new SerializedCommitAcceptor(orleans).AcceptAsync(ExpectedV2Wire());
        AssertUnsupported(orleansWire);
        Assert.Equal(0, observed.ProviderWriteCalls);
    }

    private static DcbDomainTypes BuildDomain()
    {
        var domain = DomainType.GetDomainTypes();
        ((SimpleEventTypes)domain.EventTypes).RegisterEventType<ExpectedMarkerEvent>();
        ((SimpleTagTypes)domain.TagTypes).RegisterTagGroupType<ExpectedMarkerTag>();
        return domain;
    }

    private static CommandExecutionOptions ExpectedOptions() =>
        new()
        {
            ExpectedTagPositions = new ExpectedTagPositionSpecification(
                [new TagHeadExpectationEntry(ServiceId, Tag, TagHeadExpectation.NoEnforcement())])
        };

    private static VersionedExpectedTagPositionSerializedCommitRequest ExpectedV2Request() =>
        new(
            VersionedExpectedTagPositionSerializedCommitRequest.CurrentVersion,
            [new SerializableEventCandidate("{}"u8.ToArray(), nameof(ExpectedMarkerEvent), [Tag])],
            [new ConsistencyTagEntry(Tag, string.Empty)],
            [new TagHeadExpectationEntry(ServiceId, Tag, TagHeadExpectation.NoEnforcement())]);

    private static byte[] ExpectedV2Wire() => SerializedCommitWireContract.SerializeToUtf8Bytes(ExpectedV2Request());

    private static CosmosDbEventStore NewCosmosStore(DcbDomainTypes domain)
    {
        var options = new CosmosDbEventStoreOptions
        {
            EventsContainerName = "events",
            TagsContainerName = "tags"
        };
        return new CosmosDbEventStore(
            new CosmosDbContext(new InMemoryCosmosClient(), "g40-ac6", null, options),
            domain.EventTypes,
            new DefaultServiceIdProvider(),
            new DefaultCosmosContainerResolver(options));
    }

    private static DynamoDbEventStore NewDynamoStore(DcbDomainTypes domain)
    {
        var client = DispatchProxy.Create<IAmazonDynamoDB, UnreachableDynamoClient>();
        var options = Options.Create(new DynamoDbEventStoreOptions
        {
            AutoCreateTables = false,
            EventsTableName = "events",
            TagsTableName = "tags",
            ProjectionStatesTableName = "states",
            WriteShardCount = 1
        });
        return new DynamoDbEventStore(new DynamoDbContext(client, options), domain.EventTypes, new DefaultServiceIdProvider());
    }

    private static void AssertUnsupported(ResultBox<ExecutionResult> result)
    {
        Assert.False(result.IsSuccess);
        AssertUnsupported(result.GetException());
    }

    private static void AssertUnsupported(ResultBox<SerializedCommitResult> result)
    {
        Assert.False(result.IsSuccess);
        AssertUnsupported(result.GetException());
    }

    private static void AssertUnsupported(Exception exception)
    {
        var unsupported = Assert.IsType<ConditionNotSupportedException>(exception);
        Assert.Equal(WriteConditionKind.ExpectedTagPosition, unsupported.RequestedKind);
    }

    private sealed record ExpectedMarkerCommand : ICommand;
    private sealed record ExpectedMarkerEvent(string Value) : IEventPayload;

    private sealed record ExpectedMarkerTag(string Id) : IStringTagGroup<ExpectedMarkerTag>
    {
        public static string TagGroupName => "ExpectedMarker";
        public static ExpectedMarkerTag FromContent(string content) => new(content);
        public bool IsConsistencyTag() => true;
        public string GetId() => Id;
    }

    private class UnreachableDynamoClient : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"Dynamo must not be invoked by a fail-closed expected-position request: {targetMethod?.Name}");
    }

    private class UnreachableClusterClient : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"Orleans must not resolve a grain before expected-position rejection: {targetMethod?.Name}");
    }
}
