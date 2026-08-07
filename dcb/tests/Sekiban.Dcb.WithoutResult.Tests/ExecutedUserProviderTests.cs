using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using Xunit;

namespace Sekiban.Dcb.WithoutResult.Tests;

/// <summary>
///     SEK-G23 executed-user provider parity acceptance tests for the WithoutResult command path.
/// </summary>
public class ExecutedUserProviderTests
{
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
        var domainTypes = CreateDomainTypes();
        var store = new InMemoryEventStore();
        var executor = CreateExecutor(store, domainTypes);

        await executor.ExecuteAsync(new CreateSingleEventCommand(Guid.NewGuid(), "x"));

        var events = (await store.ReadAllEventsAsync()).GetValue().ToList();
        Assert.Single(events);
        Assert.Equal("GeneralSekibanExecutor", events[0].EventMetadata.ExecutedUser);
    }

    [Fact]
    public async Task RegisteredProvider_Applies_Value_To_Command_Path()
    {
        var domainTypes = CreateDomainTypes();
        var store = new InMemoryEventStore();
        var executor = CreateExecutor(store, domainTypes, new ConstantProvider("subscriber@example.com"));

        await executor.ExecuteAsync(new CreateSingleEventCommand(Guid.NewGuid(), "x"));

        var events = (await store.ReadAllEventsAsync()).GetValue().ToList();
        Assert.Single(events);
        Assert.Equal("subscriber@example.com", events[0].EventMetadata.ExecutedUser);
    }

    [Fact]
    public async Task Provider_Evaluated_Once_Per_Command_And_Reused_For_Multiple_Events()
    {
        var domainTypes = CreateDomainTypes();
        var store = new InMemoryEventStore();
        var sequence = new SequenceProvider("user-a", "user-b");
        var executor = CreateExecutor(store, domainTypes, sequence);

        await executor.ExecuteAsync(new CreateMultiEventCommand(Guid.NewGuid()));
        await executor.ExecuteAsync(new CreateSingleEventCommand(Guid.NewGuid(), "second"));

        Assert.Equal(2, sequence.CallCount);

        var events = (await store.ReadAllEventsAsync()).GetValue().OrderBy(e => e.SortableUniqueIdValue).ToList();
        Assert.Equal(3, events.Count);

        Assert.All(events.Take(2), e => Assert.Equal("user-a", e.EventMetadata.ExecutedUser));
        Assert.Equal("user-b", events[2].EventMetadata.ExecutedUser);
    }

    [Fact]
    public async Task NullOrEmptyProvider_FallsBackTo_Default_Literal()
    {
        var domainTypes = CreateDomainTypes();
        var store = new InMemoryEventStore();
        var executor = CreateExecutor(store, domainTypes, new ConstantProvider(null));

        await executor.ExecuteAsync(new CreateSingleEventCommand(Guid.NewGuid(), "x"));

        var events = (await store.ReadAllEventsAsync()).GetValue().ToList();
        Assert.Single(events);
        Assert.Equal("GeneralSekibanExecutor", events[0].EventMetadata.ExecutedUser);
    }

    [Fact]
    public async Task Provider_Resolves_As_Optional_Dependency_From_Same_ServiceProvider()
    {
        var domainTypes = CreateDomainTypes();
        var store = new InMemoryEventStore();
        var provider = new ConstantProvider("di-user");
        var services = new ServiceCollection();
        services.AddSingleton<IEventStore>(store);
        services.AddSingleton(domainTypes);
        services.AddSingleton<IExecutedUserProvider>(provider);
        services.AddTransient<IActorObjectAccessor>(sp =>
            new InMemoryObjectAccessor(sp.GetRequiredService<IEventStore>(), sp.GetRequiredService<DcbDomainTypes>()));
        services.AddTransient<GeneralSekibanExecutor>();

        var sp = services.BuildServiceProvider();
        var executor = sp.GetRequiredService<GeneralSekibanExecutor>();

        await executor.ExecuteAsync(new CreateSingleEventCommand(Guid.NewGuid(), "di"));

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
        public static Task<EventOrNone> HandleAsync(CreateSingleEventCommand command, ICommandContext context) =>
            Task.FromResult(EventOrNone.From(new TestCreated { Id = command.Id, Name = command.Name }, new TestTag(command.Id)));
    }

    private sealed record CreateMultiEventCommand(Guid Id) : ICommandWithHandler<CreateMultiEventCommand>
    {
        public static async Task<EventOrNone> HandleAsync(CreateMultiEventCommand command, ICommandContext context)
        {
            var tag = new TestTag(command.Id);
            await context.AppendEvent(new TestCreated { Id = command.Id, Name = "first" }, tag);
            await context.AppendEvent(new TestAdded { Id = command.Id }, tag);
            return EventOrNone.Empty;
        }
    }
}
