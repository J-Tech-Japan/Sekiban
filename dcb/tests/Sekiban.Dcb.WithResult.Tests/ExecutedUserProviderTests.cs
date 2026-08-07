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
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Sekiban.Dcb.Tests.Cosmos;
using Xunit;

namespace Sekiban.Dcb.Tests;

/// <summary>
///     SEK-G23 executed-user provider acceptance tests for the WithResult command path.
/// </summary>
public class ExecutedUserProviderTests : IDisposable
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly string _dbPath;

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

    private static GeneralSekibanExecutor CreateExecutor(
        IEventStore store,
        DcbDomainTypes domainTypes,
        IExecutedUserProvider? provider = null)
    {
        var accessor = new InMemoryObjectAccessor(store, domainTypes);
        return new GeneralSekibanExecutor(store, accessor, domainTypes, null, provider);
    }

    [Fact]
    public async Task AbsentProvider_Preserves_Default_Literal()
    {
        var store = new Sekiban.Dcb.Testing.InMemoryEventStore(_domainTypes.EventTypes);
        var executor = CreateExecutor(store, _domainTypes);

        var result = await executor.ExecuteAsync(new CreateSingleEventCommand(Guid.NewGuid(), "x"));
        Assert.True(result.IsSuccess);

        var events = (await store.ReadAllEventsAsync()).GetValue().ToList();
        Assert.Single(events);
        Assert.Equal("GeneralSekibanExecutor", events[0].EventMetadata.ExecutedUser);
    }

    [Fact]
    public async Task NullOrEmptyProvider_FallsBackTo_Default_Literal()
    {
        var store = new Sekiban.Dcb.Testing.InMemoryEventStore(_domainTypes.EventTypes);
        var executor = CreateExecutor(store, _domainTypes, new ConstantProvider(""));

        var result = await executor.ExecuteAsync(new CreateSingleEventCommand(Guid.NewGuid(), "x"));
        Assert.True(result.IsSuccess);

        var events = (await store.ReadAllEventsAsync()).GetValue().ToList();
        Assert.Single(events);
        Assert.Equal("GeneralSekibanExecutor", events[0].EventMetadata.ExecutedUser);
    }

    [Fact]
    public async Task RegisteredProvider_Applies_Value_To_Command_Path()
    {
        var store = new Sekiban.Dcb.Testing.InMemoryEventStore(_domainTypes.EventTypes);
        var executor = CreateExecutor(store, _domainTypes, new ConstantProvider("admin@example.com"));

        var result = await executor.ExecuteAsync(new CreateSingleEventCommand(Guid.NewGuid(), "x"));
        Assert.True(result.IsSuccess);

        var events = (await store.ReadAllEventsAsync()).GetValue().ToList();
        Assert.Single(events);
        Assert.Equal("admin@example.com", events[0].EventMetadata.ExecutedUser);
    }

    [Fact]
    public async Task Provider_Evaluated_Once_Per_Command_And_Reused_For_Multiple_Events()
    {
        var store = new Sekiban.Dcb.Testing.InMemoryEventStore(_domainTypes.EventTypes);
        var sequence = new SequenceProvider("user-1", "user-2");
        var executor = CreateExecutor(store, _domainTypes, sequence);

        var id1 = Guid.NewGuid();
        var multi = await executor.ExecuteAsync(new CreateMultiEventCommand(id1));
        Assert.True(multi.IsSuccess);

        var id2 = Guid.NewGuid();
        var single = await executor.ExecuteAsync(new CreateSingleEventCommand(id2, "second"));
        Assert.True(single.IsSuccess);

        Assert.Equal(2, sequence.CallCount);

        var events = (await store.ReadAllEventsAsync()).GetValue().OrderBy(e => e.SortableUniqueIdValue).ToList();
        Assert.Equal(3, events.Count);

        var multiEvents = events.Take(2).ToList();
        Assert.All(multiEvents, e => Assert.Equal("user-1", e.EventMetadata.ExecutedUser));

        var singleEvent = events.Last();
        Assert.IsType<TestCreated>(singleEvent.Payload);
        Assert.Equal("user-2", singleEvent.EventMetadata.ExecutedUser);
    }

    [Fact]
    public async Task NoEvent_Command_Does_Not_Call_Provider()
    {
        var store = new Sekiban.Dcb.Testing.InMemoryEventStore(_domainTypes.EventTypes);
        var sequence = new SequenceProvider("user-1");
        var executor = CreateExecutor(store, _domainTypes, sequence);

        var result = await executor.ExecuteAsync(new NoEventCommand());
        Assert.True(result.IsSuccess);

        Assert.Equal(0, sequence.CallCount);
        Assert.Empty((await store.ReadAllEventsAsync()).GetValue());
    }

    [Fact]
    public async Task Serialized_Path_Keeps_Pinned_Literal_And_Does_Not_Call_Provider()
    {
        var store = new Sekiban.Dcb.Testing.InMemoryEventStore(_domainTypes.EventTypes);
        var throwing = new ThrowingProvider();
        var executor = (ISerializedSekibanDcbExecutor)CreateExecutor(store, _domainTypes, throwing);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new TestCreated { Id = Guid.NewGuid(), Name = "via-wasm" }, _domainTypes.JsonSerializerOptions);
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
        var domainTypes = CreateDomainTypes();
        var writeStore = new SqliteEventStore(_dbPath, domainTypes.EventTypes);
        var executor = CreateExecutor(writeStore, domainTypes, new ConstantProvider("subscriber-123"));

        var id = Guid.NewGuid();
        var result = await executor.ExecuteAsync(new CreateSingleEventCommand(id, "sql"));
        Assert.True(result.IsSuccess);

        var readStore = new SqliteEventStore(_dbPath, domainTypes.EventTypes);
        var events = (await readStore.ReadAllEventsAsync()).GetValue().ToList();
        Assert.Single(events);
        Assert.Equal("subscriber-123", events[0].EventMetadata.ExecutedUser);
    }

    [Fact]
    public async Task RoundTrip_Cosmos_Preserves_ExecutedUser()
    {
        var domainTypes = CreateDomainTypes();
        var client = new InMemoryCosmosClient();
        var options = new CosmosDbEventStoreOptions
        {
            EventsContainerName = "events",
            TagsContainerName = "tags",
            WriteFailurePolicy = CosmosWriteFailurePolicy.RollForward
        };
        var context = new CosmosDbContext(client, "test-db", null, options);
        var resolver = new DefaultCosmosContainerResolver(options);
        var store = new CosmosDbEventStore(context, domainTypes.EventTypes, new FixedServiceIdProvider("svc"), resolver);
        var executor = CreateExecutor(store, domainTypes, new ConstantProvider("entra-admin@contoso.com"));

        var id = Guid.NewGuid();
        var result = await executor.ExecuteAsync(new CreateSingleEventCommand(id, "cosmos"));
        Assert.True(result.IsSuccess);

        var events = (await store.ReadAllEventsAsync()).GetValue().ToList();
        Assert.Single(events);
        Assert.Equal("entra-admin@contoso.com", events[0].EventMetadata.ExecutedUser);
    }

    [Fact]
    public async Task Provider_Resolves_As_Optional_Dependency_From_Same_ServiceProvider()
    {
        var store = new Sekiban.Dcb.Testing.InMemoryEventStore(_domainTypes.EventTypes);
        var provider = new ConstantProvider("di-user");
        var services = new ServiceCollection();
        services.AddSingleton<IEventStore>(store);
        services.AddSingleton(_domainTypes);
        services.AddSingleton<IExecutedUserProvider>(provider);
        services.AddTransient<IActorObjectAccessor>(sp =>
            new InMemoryObjectAccessor(sp.GetRequiredService<IEventStore>(), sp.GetRequiredService<DcbDomainTypes>()));
        services.AddTransient<GeneralSekibanExecutor>();

        var sp = services.BuildServiceProvider();
        var executor = sp.GetRequiredService<GeneralSekibanExecutor>();

        var result = await executor.ExecuteAsync(new CreateSingleEventCommand(Guid.NewGuid(), "di"));
        Assert.True(result.IsSuccess);

        var events = (await store.ReadAllEventsAsync()).GetValue().ToList();
        Assert.Single(events);
        Assert.Equal("di-user", events[0].EventMetadata.ExecutedUser);
    }

    private sealed class ConstantProvider : IExecutedUserProvider
    {
        private readonly string? _value;
        public ConstantProvider(string? value) => _value = value;
        public string GetExecutedUser() => _value ?? string.Empty;
    }

    private sealed class SequenceProvider : IExecutedUserProvider
    {
        private readonly IReadOnlyList<string> _values;
        private int _index;
        public SequenceProvider(params string[] values) => _values = values;
        public int CallCount => _index;
        public string GetExecutedUser()
        {
            var value = _index < _values.Count ? _values[_index] : $"extra-{_index}";
            _index++;
            return value;
        }
    }

    private sealed class ThrowingProvider : IExecutedUserProvider
    {
        public string GetExecutedUser() => throw new InvalidOperationException("Provider must not be consulted on the serialized path.");
    }

    private sealed record TestTag : ITagGroup<TestTag>
    {
        private readonly Guid _id;
        public TestTag(Guid id) => _id = id;
        public bool IsConsistencyTag() => false;
        public static string TagGroupName => "Test";
        public string GetTag() => $"Test:{_id}";
        public string GetTagContent() => _id.ToString();
        public static TestTag FromContent(string content) => new(Guid.Parse(content));
    }

    public sealed record TestCreated : IEventPayload
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
    }

    public sealed record TestAdded : IEventPayload
    {
        public Guid Id { get; init; }
    }

    private sealed record CreateSingleEventCommand(Guid Id, string Name) : ICommandWithHandler<CreateSingleEventCommand>
    {
        public static Task<ResultBox<EventOrNone>> HandleAsync(CreateSingleEventCommand command, ICommandContext context) =>
            ResultBox.Start
                .Remap(_ => new TestTag(command.Id))
                .Conveyor(tag => EventOrNone.EventWithTags(new TestCreated { Id = command.Id, Name = command.Name }, tag))
                .ToTask();
    }

    private sealed record CreateMultiEventCommand(Guid Id) : ICommandWithHandler<CreateMultiEventCommand>
    {
        public static async Task<ResultBox<EventOrNone>> HandleAsync(CreateMultiEventCommand command, ICommandContext context)
        {
            var tag = new TestTag(command.Id);
            await context.AppendEvent(new TestCreated { Id = command.Id, Name = "first" }, tag);
            await context.AppendEvent(new TestAdded { Id = command.Id }, tag);
            return EventOrNone.Empty;
        }
    }

    private sealed record NoEventCommand : ICommandWithHandler<NoEventCommand>
    {
        public static Task<ResultBox<EventOrNone>> HandleAsync(NoEventCommand command, ICommandContext context) =>
            Task.FromResult(ResultBox.FromValue(EventOrNone.Empty));
    }

    private sealed class FixedServiceIdProvider : IServiceIdProvider
    {
        private readonly string _serviceId;
        public FixedServiceIdProvider(string serviceId) => _serviceId = serviceId;
        public string GetCurrentServiceId() => _serviceId;
    }
}
