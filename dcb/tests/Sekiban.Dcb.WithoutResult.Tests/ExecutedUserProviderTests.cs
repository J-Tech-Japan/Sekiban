using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Testing;
using Sekiban.Dcb.TestSupport.ExecutedUser;
using Xunit;
namespace Sekiban.Dcb.WithoutResult.Tests;

/// <summary>
/// SEK-G23 executed-user provider parity acceptance tests for the WithoutResult command path.
/// Common scenarios live in <see cref="ExecutedUserProviderScenarioTestsBase"/>.
/// </summary>
public class ExecutedUserProviderTests : ExecutedUserProviderScenarioTestsBase
{
    private readonly DcbDomainTypes _domainTypes;
    private InMemoryEventStore _store = null!;
    private GeneralSekibanExecutor _executor = null!;

    public ExecutedUserProviderTests()
    {
        _domainTypes = CreateDomainTypes();
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
        _store = new InMemoryEventStore(_domainTypes.EventTypes);
        var accessor = new InMemoryObjectAccessor(_store, _domainTypes);
        _executor = new GeneralSekibanExecutor(_store, accessor, _domainTypes, null, provider);
        return Task.CompletedTask;
    }

    private static Task<EventOrNone> HandleSingleEventAsync(
        CreateSingleEventTestCommand command,
        ICommandContext context) =>
        Task.FromResult(EventOrNone.From(new TestCreated { Id = command.Id, Name = command.Name }, new TestTag(command.Id)));

    private static async Task<EventOrNone> HandleMultiEventAsync(
        CreateMultiEventTestCommand command,
        ICommandContext context)
    {
        var tag = new TestTag(command.Id);
        await context.AppendEvent(new TestCreated { Id = command.Id, Name = "first" }, tag);
        await context.AppendEvent(new TestAdded { Id = command.Id }, tag);
        return EventOrNone.Empty;
    }

    private static Task<EventOrNone> HandleNoEventAsync(
        NoEventTestCommand _,
        ICommandContext __) =>
        Task.FromResult(EventOrNone.Empty);

    protected override Task ExecuteSingleEventAsync(string name = "x") =>
        _executor.ExecuteAsync(new CreateSingleEventTestCommand(Guid.NewGuid(), name), HandleSingleEventAsync);

    protected override Task ExecuteMultiEventAsync() =>
        _executor.ExecuteAsync(new CreateMultiEventTestCommand(Guid.NewGuid()), HandleMultiEventAsync);

    protected override Task ExecuteNoEventAsync() =>
        _executor.ExecuteAsync(new NoEventTestCommand(), HandleNoEventAsync);

    protected override async Task<IReadOnlyList<Event>> ReadAllEventsAsync() =>
        (await _store.ReadAllEventsAsync()).GetValue().ToList();

    protected override async Task ExecuteSingleEventViaDiAsync(IExecutedUserProvider provider, string name = "x")
    {
        var store = new InMemoryEventStore(_domainTypes.EventTypes);
        _store = store;
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
}
