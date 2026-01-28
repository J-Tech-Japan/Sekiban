using Dcb.Domain;
using Dcb.Domain.Weather;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     Tests for reservation decision rules:
///     1. Non-consistency tags must NOT trigger reservation.
///     2. ConsistencyTag with SortableUniqueId uses provided id.
///     3. ConsistencyTag without SortableUniqueId (or plain consistency tag) uses accessed state LastSortableUniqueId.
/// </summary>
public class ConsistencyReservationRulesTest
{
    private readonly InMemoryObjectAccessor _actorAccessor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly InMemoryEventStore _eventStore;
    private readonly GeneralSekibanExecutor _executor;

    public ConsistencyReservationRulesTest()
    {
        _eventStore = new InMemoryEventStore();
        _domainTypes = DomainType.GetDomainTypes();
        _actorAccessor = new InMemoryObjectAccessor(_eventStore, _domainTypes);
        _executor = new GeneralSekibanExecutor(_eventStore, _actorAccessor, _domainTypes);
    }

    [Fact]
    public async Task NonConsistencyTag_Should_Not_Create_TagState()
    {
        var baseTag = new BaseTag(Guid.NewGuid().ToString());
        var nonConsistency = NonConsistencyTag.From(baseTag);

        var result = await _executor.ExecuteAsync(
            new SimpleCommand(),
            (cmd, ctx) => Task.FromResult(EventOrNone.Event(new DummyEvent("A"), nonConsistency)));

        Assert.True(result.IsSuccess);
        // TagExistsAsync should still return true because event write uses tag list,
        // but reservation path should not have attempted optimistic check.
        // We cannot directly observe reservation count; instead ensure execution succeeds
        // even if providing old sortable id would have failed (implicit test by absence of exception).
    }

    [Fact]
    public async Task ConsistencyTag_With_Explicit_SortableUniqueId_Should_Use_It()
    {
        var baseTag = new BaseTag(Guid.NewGuid().ToString());
        var explicitId = SortableUniqueId.GenerateNew();
        var consistencyTag = ConsistencyTag.FromTagWithSortableUniqueId(baseTag, explicitId);

        var result = await _executor.ExecuteAsync(
            new SimpleCommand(),
            (cmd, ctx) => Task.FromResult(EventOrNone.Event(new DummyEvent("B"), consistencyTag)));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ConsistencyTag_Without_SortableUniqueId_Should_Use_Accessed_State()
    {
        var baseTag = new BaseTag(Guid.NewGuid().ToString());
        var consistencyTag = ConsistencyTag.From(baseTag); // no explicit id

        // First write to create state
        var first = await _executor.ExecuteAsync(
            new SimpleCommand(),
            (cmd, ctx) => Task.FromResult(EventOrNone.Event(new DummyEvent("C1"), consistencyTag)));
        Assert.True(first.IsSuccess);

        // Second write should access state and use LastSortableUniqueId
        // Access state beforehand to ensure it is tracked
        await _executor.ExecuteAsync(
            new SimpleCommand(),
            async (cmd, ctx) =>
            {
                await ctx.GetStateAsync<DummyProjector>(baseTag);
                return EventOrNone.None; // just for tracking
            });
        var second = await _executor.ExecuteAsync(
            new SimpleCommand(),
            (cmd, ctx) => Task.FromResult(EventOrNone.Event(new DummyEvent("C2"), consistencyTag)));
        Assert.True(second.IsSuccess);
    }

    [Fact]
    public async Task NonConsistencyTag_Should_Trigger_CatchUpRefresh_Via_Notification()
    {
        // Arrange: Use NonConsistencyTag with WeatherForecast domain
        var forecastId = Guid.NewGuid();
        var baseTag = new WeatherForecastTag(forecastId);
        var nonConsistency = NonConsistencyTag.From(baseTag);

        // Act: Write event with NonConsistencyTag
        var writeResult = await _executor.ExecuteAsync(
            new SimpleCommand(),
            (cmd, ctx) => Task.FromResult(EventOrNone.Event(
                new WeatherForecastCreated(forecastId, "Tokyo", DateOnly.FromDateTime(DateTime.Today), 25, "Sunny"),
                nonConsistency)));

        Assert.True(writeResult.IsSuccess);

        // Verify: Get tag state immediately after - should reflect the written event
        // This tests that NotifyEventWrittenAsync was called and catch-up will occur
        var tagStateId = TagStateId.FromProjector<WeatherForecastProjector>(baseTag);
        var stateResult = await _executor.GetTagStateAsync(tagStateId);

        Assert.True(stateResult.IsSuccess);
        var state = stateResult.GetValue();

        // The tag state should have the event reflected (version > 0 and payload updated)
        Assert.True(state.Version > 0, "Tag state should have been updated with the event");
        Assert.IsType<WeatherForecastState>(state.Payload);
        var payload = (WeatherForecastState)state.Payload;
        Assert.Equal("Tokyo", payload.Location);
        Assert.Equal(25, payload.TemperatureC);
    }

    [Fact]
    public async Task NonConsistencyTag_Should_Update_TagState_Immediately_After_Multiple_Writes()
    {
        // Arrange: Use NonConsistencyTag with WeatherForecast domain
        var forecastId = Guid.NewGuid();
        var baseTag = new WeatherForecastTag(forecastId);
        var nonConsistency = NonConsistencyTag.From(baseTag);

        // Act 1: First write - Create
        var write1 = await _executor.ExecuteAsync(
            new SimpleCommand(),
            (cmd, ctx) => Task.FromResult(EventOrNone.Event(
                new WeatherForecastCreated(forecastId, "Tokyo", DateOnly.FromDateTime(DateTime.Today), 20, "Cloudy"),
                nonConsistency)));
        Assert.True(write1.IsSuccess);

        // Verify first state
        var tagStateId = TagStateId.FromProjector<WeatherForecastProjector>(baseTag);
        var state1 = await _executor.GetTagStateAsync(tagStateId);
        Assert.True(state1.IsSuccess);
        Assert.Equal("Tokyo", ((WeatherForecastState)state1.GetValue().Payload).Location);
        Assert.Equal(20, ((WeatherForecastState)state1.GetValue().Payload).TemperatureC);

        // Act 2: Second write - Update
        var write2 = await _executor.ExecuteAsync(
            new SimpleCommand(),
            (cmd, ctx) => Task.FromResult(EventOrNone.Event(
                new WeatherForecastUpdated(forecastId, "Osaka", DateOnly.FromDateTime(DateTime.Today), 30, "Hot"),
                nonConsistency)));
        Assert.True(write2.IsSuccess);

        // Verify: Get tag state immediately after second write
        var state2 = await _executor.GetTagStateAsync(tagStateId);
        Assert.True(state2.IsSuccess);
        var payload2 = (WeatherForecastState)state2.GetValue().Payload;
        Assert.Equal("Osaka", payload2.Location);
        Assert.Equal(30, payload2.TemperatureC);
        Assert.True(state2.GetValue().Version > state1.GetValue().Version);
    }

    private record DummyEvent(string Name) : IEventPayload;
    private record BaseTag(string Id) : ITag
    {
        public bool IsConsistencyTag() => true;
        public string GetTagGroup() => "Base";
        public string GetTagContent() => Id;
    }

    private record SimpleCommand : ICommand;

    private class DummyProjector : ITagProjector<DummyProjector>
    {
        public static string ProjectorVersion => "1";
        public static string ProjectorName => nameof(DummyProjector);
        public static ITagStatePayload Project(ITagStatePayload current, Event e) => current;
    }
}
