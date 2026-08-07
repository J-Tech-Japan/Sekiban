using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.CosmosDb;
using Sekiban.Dcb.CosmosDb.Models;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Sqlite;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Testing;
using Sekiban.Dcb.TestSupport.ExecutedUser;
using Sekiban.Dcb.Tests.Cosmos;
using Xunit;
namespace Sekiban.Dcb.Tests;

/// <summary>
/// SEK-G23 executed-user provider acceptance tests for the WithResult command path.
/// Common scenarios live in <see cref="ExecutedUserProviderScenarioTestsBase"/>.
/// </summary>
public class ExecutedUserProviderTests : ExecutedUserProviderScenarioTestsBase, IDisposable
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly string _dbPath;
    private Sekiban.Dcb.Testing.InMemoryEventStore _memoryStore = null!;
    private GeneralSekibanExecutor _executor = null!;

    public ExecutedUserProviderTests()
    {
        _domainTypes = CreateDomainTypes();
        _dbPath = Path.Combine(Path.GetTempPath(), $"sek-g23-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private static DcbDomainTypes CreateDomainTypes() =>
        DcbDomainTypesExtensions.Simple(b =>
        {
            b.EventTypes.RegisterEventType<TestCreated>();
            b.EventTypes.RegisterEventType<TestAdded>();
            b.TagTypes.RegisterTagGroupType<TestTag>();
        });

    protected override Task BeginScenarioAsync(IExecutedUserProvider? provider)
    {
        _memoryStore = new Sekiban.Dcb.Testing.InMemoryEventStore(_domainTypes.EventTypes);
        var accessor = new InMemoryObjectAccessor(_memoryStore, _domainTypes);
        _executor = new GeneralSekibanExecutor(_memoryStore, accessor, _domainTypes, null, provider);
        return Task.CompletedTask;
    }

    private static Task<ResultBox<EventOrNone>> HandleSingleEventAsync(
        CreateSingleEventTestCommand command,
        ICommandContext context) =>
        ResultBox.Start
            .Remap(_ => new TestTag(command.Id))
            .Conveyor(tag => EventOrNone.EventWithTags(new TestCreated { Id = command.Id, Name = command.Name }, tag))
            .ToTask();

    private static async Task<ResultBox<EventOrNone>> HandleMultiEventAsync(
        CreateMultiEventTestCommand command,
        ICommandContext context)
    {
        var tag = new TestTag(command.Id);
        await context.AppendEvent(new TestCreated { Id = command.Id, Name = "first" }, tag);
        await context.AppendEvent(new TestAdded { Id = command.Id }, tag);
        return EventOrNone.Empty;
    }

    private static Task<ResultBox<EventOrNone>> HandleNoEventAsync(
        NoEventTestCommand _,
        ICommandContext __) =>
        Task.FromResult(ResultBox.FromValue(EventOrNone.Empty));

    protected override Task ExecuteSingleEventAsync(string name = "x") =>
        _executor.ExecuteAsync(new CreateSingleEventTestCommand(Guid.NewGuid(), name), HandleSingleEventAsync);

    protected override Task ExecuteMultiEventAsync() =>
        _executor.ExecuteAsync(new CreateMultiEventTestCommand(Guid.NewGuid()), HandleMultiEventAsync);

    protected override Task ExecuteNoEventAsync() =>
        _executor.ExecuteAsync(new NoEventTestCommand(), HandleNoEventAsync);

    protected override async Task<IReadOnlyList<Event>> ReadAllEventsAsync() =>
        (await _memoryStore.ReadAllEventsAsync()).GetValue().ToList();

    protected override async Task ExecuteSingleEventViaDiAsync(IExecutedUserProvider provider, string name = "x")
    {
        var store = new Sekiban.Dcb.Testing.InMemoryEventStore(_domainTypes.EventTypes);
        _memoryStore = store;
        var services = new ServiceCollection();
        services.AddSingleton<IEventStore>(store);
        services.AddSingleton(_domainTypes);
        services.AddSingleton(provider);
        services.AddTransient<IActorObjectAccessor>(sp =>
            new InMemoryObjectAccessor(sp.GetRequiredService<IEventStore>(), sp.GetRequiredService<DcbDomainTypes>()));
        services.AddTransient<GeneralSekibanExecutor>();

        var sp = services.BuildServiceProvider();
        _executor = sp.GetRequiredService<GeneralSekibanExecutor>();
        await _executor.ExecuteAsync(new CreateSingleEventTestCommand(Guid.NewGuid(), name), HandleSingleEventAsync);
    }

    [Fact]
    public async Task Serialized_Path_Keeps_Pinned_Literal_And_Does_Not_Call_Provider()
    {
        await BeginScenarioAsync(new ThrowingExecutedUserProvider());
        var executor = (ISerializedSekibanDcbExecutor)_executor;

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new TestCreated { Id = Guid.NewGuid(), Name = "via-wasm" },
            _domainTypes.JsonSerializerOptions);
        var tagString = $"Test:{Guid.NewGuid()}";
        var request = new SerializedCommitRequest(
            [new SerializableEventCandidate(payload, nameof(TestCreated), [tagString])],
            []);

        var result = await executor.CommitSerializableEventsAsync(request);
        Assert.True(result.IsSuccess);

        var written = result.GetValue().WrittenEvents;
        Assert.Single(written);
        Assert.Equal("SerializedSekibanExecutor", written[0].EventMetadata.ExecutedUser);
    }

    [Fact]
    public async Task RoundTrip_Sqlite_Preserves_ExecutedUser()
    {
        var writeStore = new SqliteEventStore(_dbPath, _domainTypes.EventTypes);
        var accessor = new InMemoryObjectAccessor(writeStore, _domainTypes);
        var executor = new GeneralSekibanExecutor(writeStore, accessor, _domainTypes, null, new ConstantExecutedUserProvider("subscriber-123"));

        await executor.ExecuteAsync(
                new CreateSingleEventTestCommand(Guid.NewGuid(), "sql"),
                HandleSingleEventAsync)
            ;

        var readStore = new SqliteEventStore(_dbPath, _domainTypes.EventTypes);
        var events = (await readStore.ReadAllEventsAsync()).GetValue().ToList();
        Assert.Single(events);
        Assert.Equal("subscriber-123", events[0].EventMetadata.ExecutedUser);
    }

    [Fact]
    public async Task RoundTrip_Cosmos_Preserves_ExecutedUser()
    {
        var client = new InMemoryCosmosClient();
        var options = new CosmosDbEventStoreOptions
        {
            EventsContainerName = "events",
            TagsContainerName = "tags",
            WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward
        };
        var context = new CosmosDbContext(client, "test-db", null, options);
        var resolver = new DefaultCosmosContainerResolver(options);
        var store = new CosmosDbEventStore(context, _domainTypes.EventTypes, new FixedServiceIdProvider("svc"), resolver);
        var accessor = new InMemoryObjectAccessor(store, _domainTypes);
        var executor = new GeneralSekibanExecutor(store, accessor, _domainTypes, null, new ConstantExecutedUserProvider("entra-admin@contoso.com"));

        await executor.ExecuteAsync(
                new CreateSingleEventTestCommand(Guid.NewGuid(), "cosmos"),
                HandleSingleEventAsync)
            ;

        var events = (await store.ReadAllEventsAsync()).GetValue().ToList();
        Assert.Single(events);
        Assert.Equal("entra-admin@contoso.com", events[0].EventMetadata.ExecutedUser);
    }

    private sealed class FixedServiceIdProvider : IServiceIdProvider
    {
        private readonly string _serviceId;
        public FixedServiceIdProvider(string serviceId) => _serviceId = serviceId;
        public string GetCurrentServiceId() => _serviceId;
    }
}
