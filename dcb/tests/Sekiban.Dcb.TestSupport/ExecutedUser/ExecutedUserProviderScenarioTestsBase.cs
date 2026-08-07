using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Xunit;
namespace Sekiban.Dcb.TestSupport.ExecutedUser;

/// <summary>
/// Shared executed-user provider acceptance scenarios for the WithResult and WithoutResult executor facades.
/// Derived classes only supply an executor and a way to read back the written events.
/// </summary>
public abstract class ExecutedUserProviderScenarioTestsBase
{
    /// <summary>Default literal used when no provider is registered or the provider returns null/empty.</summary>
    protected virtual string DefaultExecutedUserLiteral => "GeneralSekibanExecutor";

    /// <summary>Prepare a store and executor for the given provider. All subsequent operations in the same test use it.</summary>
    protected abstract Task BeginScenarioAsync(IExecutedUserProvider? provider);

    /// <summary>Execute a single-event command using the executor prepared by <see cref="BeginScenarioAsync"/>.</summary>
    protected abstract Task ExecuteSingleEventAsync(string name = "x");

    /// <summary>Execute a multi-event command using the executor prepared by <see cref="BeginScenarioAsync"/>.</summary>
    protected abstract Task ExecuteMultiEventAsync();

    /// <summary>Execute a no-event command using the executor prepared by <see cref="BeginScenarioAsync"/>.</summary>
    protected virtual Task ExecuteNoEventAsync() =>
        throw new NotSupportedException("No-event scenarios are not supported by this facade.");

    /// <summary>Read all events written by the executor prepared by <see cref="BeginScenarioAsync"/>.</summary>
    protected abstract Task<IReadOnlyList<Event>> ReadAllEventsAsync();

    /// <summary>Execute a single-event command resolved from a service provider and prepare the store for reads.</summary>
    protected virtual Task ExecuteSingleEventViaDiAsync(IExecutedUserProvider provider, string name = "x") =>
        throw new NotSupportedException("DI resolution scenarios are not supported by this facade.");

    [Fact]
    public async Task AbsentProvider_Preserves_Default_Literal()
    {
        await BeginScenarioAsync(null);
        await ExecuteSingleEventAsync();
        var events = await ReadAllEventsAsync();
        Assert.Single(events);
        Assert.Equal(DefaultExecutedUserLiteral, events[0].EventMetadata.ExecutedUser);
    }

    [Fact]
    public async Task RegisteredProvider_Applies_Value_To_Command_Path()
    {
        await BeginScenarioAsync(new ConstantExecutedUserProvider("admin@example.com"));
        await ExecuteSingleEventAsync();
        var events = await ReadAllEventsAsync();
        Assert.Single(events);
        Assert.Equal("admin@example.com", events[0].EventMetadata.ExecutedUser);
    }

    [Fact]
    public async Task Provider_Evaluated_Once_Per_Command_And_Reused_For_Multiple_Events()
    {
        var sequence = new SequenceExecutedUserProvider("user-1", "user-2");
        await BeginScenarioAsync(sequence);

        await ExecuteMultiEventAsync();
        await ExecuteSingleEventAsync("second");

        Assert.Equal(2, sequence.CallCount);

        var events = (await ReadAllEventsAsync()).OrderBy(e => e.SortableUniqueIdValue).ToList();
        Assert.Equal(3, events.Count);
        Assert.All(events.Take(2), e => Assert.Equal("user-1", e.EventMetadata.ExecutedUser));
        Assert.Equal("user-2", events[2].EventMetadata.ExecutedUser);
    }

    [Fact]
    public async Task NullOrEmptyProvider_FallsBackTo_Default_Literal()
    {
        await BeginScenarioAsync(new ConstantExecutedUserProvider(string.Empty));
        await ExecuteSingleEventAsync();
        var events = await ReadAllEventsAsync();
        Assert.Single(events);
        Assert.Equal(DefaultExecutedUserLiteral, events[0].EventMetadata.ExecutedUser);
    }

    [Fact]
    public async Task DirectNullReturningProvider_FallsBackTo_Default_Literal()
    {
        await BeginScenarioAsync(new NullExecutedUserProvider());
        await ExecuteSingleEventAsync();
        var events = await ReadAllEventsAsync();
        Assert.Single(events);
        Assert.Equal(DefaultExecutedUserLiteral, events[0].EventMetadata.ExecutedUser);
    }

    [Fact]
    public async Task NoEvent_Command_Does_Not_Call_Provider()
    {
        var sequence = new SequenceExecutedUserProvider("user-1");
        await BeginScenarioAsync(sequence);
        await ExecuteNoEventAsync();
        Assert.Equal(0, sequence.CallCount);
        Assert.Empty(await ReadAllEventsAsync());
    }

    [Fact]
    public async Task Provider_Resolves_As_Optional_Dependency_From_Same_ServiceProvider()
    {
        await ExecuteSingleEventViaDiAsync(new ConstantExecutedUserProvider("di-user"));
        var events = await ReadAllEventsAsync();
        Assert.Single(events);
        Assert.Equal("di-user", events[0].EventMetadata.ExecutedUser);
    }
}
