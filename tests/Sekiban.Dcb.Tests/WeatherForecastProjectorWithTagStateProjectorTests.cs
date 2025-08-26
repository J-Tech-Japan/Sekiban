using Dcb.Domain.Projections;
using Dcb.Domain.Weather;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using System.Text.Json;
namespace Sekiban.Dcb.Tests;

public class WeatherForecastProjectorWithTagStateProjectorTests
{
    private readonly WeatherForecastProjectorWithTagStateProjector _projector;
    private readonly DcbDomainTypes _domainTypes;

    public WeatherForecastProjectorWithTagStateProjectorTests()
    {
        _projector = WeatherForecastProjectorWithTagStateProjector.GenerateInitialPayload();
        
        // Create a minimal DomainTypes for testing
        _domainTypes = new DcbDomainTypes(
            new SimpleEventTypes(),
            new SimpleTagTypes(),
            new SimpleTagProjectorTypes(),
            new SimpleTagStatePayloadTypes(),
            new SimpleMultiProjectorTypes(),
            new SimpleQueryTypes(),
            new JsonSerializerOptions());
    }

    [Fact]
    public void Projector_CreatesTagStateWithWeatherForecastState()
    {
        // Arrange
        var forecastId = Guid.NewGuid();
        var weatherTag = new WeatherForecastTag(forecastId);

        var createEvent = CreateEvent(
            new WeatherForecastCreated(forecastId, "Tokyo", DateOnly.FromDateTime(DateTime.UtcNow), 25, "Sunny"),
            DateTime.UtcNow.AddSeconds(-30) // Safe event
        );

        // Act
        var result = WeatherForecastProjectorWithTagStateProjector.Project(
            _projector,
            createEvent,
            new List<ITag> { weatherTag },
            _domainTypes, TimeProvider.System);
        var afterCreate = result.GetValue();

        // Assert
        Assert.True(result.IsSuccess);

        // Check TagState was created
        var tagStates = afterCreate.GetCurrentTagStates();
        Assert.Single(tagStates);
        Assert.Contains(forecastId, tagStates.Keys);

        var tagState = tagStates[forecastId];
        Assert.NotNull(tagState);
        Assert.IsType<WeatherForecastState>(tagState.Payload);

        var weatherState = (WeatherForecastState)tagState.Payload;
        Assert.Equal(forecastId, weatherState.ForecastId);
        Assert.Equal("Tokyo", weatherState.Location);
        Assert.Equal(25, weatherState.TemperatureC);
        Assert.Equal("Sunny", weatherState.Summary);
        Assert.False(weatherState.IsDeleted);
    }

    [Fact]
    public void Projector_UpdatesExistingTagState()
    {
        // Arrange
        var forecastId = Guid.NewGuid();
        var tag = new WeatherForecastTag(forecastId);

        var createEvent = CreateEvent(
            new WeatherForecastCreated(forecastId, "Tokyo", DateOnly.FromDateTime(DateTime.UtcNow), 20, "Cloudy"),
            DateTime.UtcNow.AddSeconds(-30));

        var updateEvent = CreateEvent(
            new WeatherForecastUpdated(forecastId, "Osaka", DateOnly.FromDateTime(DateTime.UtcNow), 30, "Sunny"),
            DateTime.UtcNow.AddSeconds(-25));

        // Act
        var result1 = WeatherForecastProjectorWithTagStateProjector.Project(
            _projector,
            createEvent,
            new List<ITag> { tag },
            _domainTypes, TimeProvider.System);
        var afterCreate = result1.GetValue();

        var result2 = WeatherForecastProjectorWithTagStateProjector.Project(
            afterCreate,
            updateEvent,
            new List<ITag> { tag },
            _domainTypes, TimeProvider.System);
        var afterUpdate = result2.GetValue();

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);

        var tagStates = afterUpdate.GetCurrentTagStates();
        Assert.Single(tagStates);

        var tagState = tagStates[forecastId];
        var weatherState = (WeatherForecastState)tagState.Payload;

        // Values should be updated
        Assert.Equal("Osaka", weatherState.Location);
        Assert.Equal(30, weatherState.TemperatureC);
        Assert.Equal("Sunny", weatherState.Summary);
        Assert.False(weatherState.IsDeleted);

        // Version should be incremented
        Assert.Equal(2, tagState.Version);
    }

    [Fact]
    public void Projector_HandlesDeleteEvents()
    {
        // Arrange
        var forecastId = Guid.NewGuid();
        var tag = new WeatherForecastTag(forecastId);

        var createEvent = CreateEvent(
            new WeatherForecastCreated(
                forecastId,
                "Tokyo",
                DateOnly.FromDateTime(DateTime.UtcNow),
                20,
                "To Be Deleted"),
            DateTime.UtcNow.AddSeconds(-30));

        var deleteEvent = CreateEvent(new WeatherForecastDeleted(forecastId), DateTime.UtcNow.AddSeconds(-25));

        // Act
        var result1 = WeatherForecastProjectorWithTagStateProjector.Project(
            _projector,
            createEvent,
            new List<ITag> { tag },
            _domainTypes, TimeProvider.System);
        var afterCreate = result1.GetValue();

        var result2 = WeatherForecastProjectorWithTagStateProjector.Project(
            afterCreate,
            deleteEvent,
            new List<ITag> { tag },
            _domainTypes, TimeProvider.System);
        var afterDelete = result2.GetValue();

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);

        // Item should be removed from current state
        var tagStates = afterDelete.GetCurrentTagStates();
        Assert.Empty(tagStates);

        // GetWeatherForecasts should also return empty
        var forecasts = afterDelete.GetWeatherForecasts().ToList();
        Assert.Empty(forecasts);
    }

    [Fact]
    public void Projector_GetWeatherForecasts_ReturnsNonDeletedForecasts()
    {
        // Arrange
        var forecastId1 = Guid.NewGuid();
        var forecastId2 = Guid.NewGuid();
        var tag1 = new WeatherForecastTag(forecastId1);
        var tag2 = new WeatherForecastTag(forecastId2);

        var createEvent1 = CreateEvent(
            new WeatherForecastCreated(forecastId1, "Tokyo", DateOnly.FromDateTime(DateTime.UtcNow), 25, "Sunny"),
            DateTime.UtcNow.AddSeconds(-30));

        var createEvent2 = CreateEvent(
            new WeatherForecastCreated(forecastId2, "Osaka", DateOnly.FromDateTime(DateTime.UtcNow), 20, "Cloudy"),
            DateTime.UtcNow.AddSeconds(-25));

        // Act
        var result1 = WeatherForecastProjectorWithTagStateProjector.Project(
            _projector,
            createEvent1,
            new List<ITag> { tag1 },
            _domainTypes, TimeProvider.System);
        var after1 = result1.GetValue();

        var result2 = WeatherForecastProjectorWithTagStateProjector.Project(
            after1,
            createEvent2,
            new List<ITag> { tag2 },
            _domainTypes, TimeProvider.System);
        var after2 = result2.GetValue();

        // Assert
        var forecasts = after2.GetWeatherForecasts().ToList();
        Assert.Equal(2, forecasts.Count);

        Assert.Contains(forecasts, f => f.ForecastId == forecastId1 && f.Location == "Tokyo");
        Assert.Contains(forecasts, f => f.ForecastId == forecastId2 && f.Location == "Osaka");
    }

    [Fact]
    public void Projector_HandlesUnsafeAndSafeStates()
    {
        // Arrange
        var forecastId = Guid.NewGuid();
        var tag = new WeatherForecastTag(forecastId);

        // Safe event (older than SafeWindow)
        var safeEvent = CreateEvent(
            new WeatherForecastCreated(forecastId, "Tokyo", DateOnly.FromDateTime(DateTime.UtcNow), 20, "Safe Weather"),
            DateTime.UtcNow.AddSeconds(-30));

        // Unsafe event (within SafeWindow)
        var unsafeEvent = CreateEvent(
            new WeatherForecastUpdated(
                forecastId,
                "Osaka",
                DateOnly.FromDateTime(DateTime.UtcNow),
                25,
                "Unsafe Weather"),
            DateTime.UtcNow.AddSeconds(-5));

        // Act
        var result1 = WeatherForecastProjectorWithTagStateProjector.Project(
            _projector,
            safeEvent,
            new List<ITag> { tag },
            _domainTypes, TimeProvider.System);
        var afterSafe = result1.GetValue();

        var result2 = WeatherForecastProjectorWithTagStateProjector.Project(
            afterSafe,
            unsafeEvent,
            new List<ITag> { tag },
            _domainTypes, TimeProvider.System);
        var afterUnsafe = result2.GetValue();

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);

        // Check if state is marked as unsafe
        Assert.True(afterUnsafe.IsTagStateUnsafe(forecastId));

        // Current state should have unsafe modifications
        var currentForecasts = afterUnsafe.GetWeatherForecasts().ToList();
        Assert.Single(currentForecasts);
        var currentForecast = currentForecasts[0];
        Assert.Equal("Osaka", currentForecast.Location);
        Assert.Equal(25, currentForecast.TemperatureC);

        // Safe state should have original values
        var safeForecasts = afterUnsafe.GetSafeWeatherForecasts().ToList();
        Assert.Single(safeForecasts);
        var safeForecast = safeForecasts[0];
        Assert.Equal("Tokyo", safeForecast.Location);
        Assert.Equal(20, safeForecast.TemperatureC);
    }

    [Fact]
    public void Projector_IgnoresUnknownEventTypes()
    {
        // Arrange
        var forecastId = Guid.NewGuid();
        var tag = new WeatherForecastTag(forecastId);

        // Create a weather forecast first
        var createEvent = CreateEvent(
            new WeatherForecastCreated(forecastId, "Tokyo", DateOnly.FromDateTime(DateTime.UtcNow), 20, "Sunny"),
            DateTime.UtcNow.AddSeconds(-30));

        // Create an unknown event type
        var unknownEvent = CreateEvent(new UnknownEventType("Some data"), DateTime.UtcNow.AddSeconds(-25));

        // Act
        var result1 = WeatherForecastProjectorWithTagStateProjector.Project(
            _projector,
            createEvent,
            new List<ITag> { tag },
            _domainTypes, TimeProvider.System);
        var afterCreate = result1.GetValue();

        var result2 = WeatherForecastProjectorWithTagStateProjector.Project(
            afterCreate,
            unknownEvent,
            new List<ITag> { tag },
            _domainTypes, TimeProvider.System);
        var afterUnknown = result2.GetValue();

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);

        // State should remain unchanged after unknown event
        var stateAfterCreate = afterCreate.GetCurrentTagStates();
        var stateAfterUnknown = afterUnknown.GetCurrentTagStates();

        Assert.Single(stateAfterCreate);
        Assert.Single(stateAfterUnknown);

        var weatherStateCreate = (WeatherForecastState)stateAfterCreate[forecastId].Payload;
        var weatherStateUnknown = (WeatherForecastState)stateAfterUnknown[forecastId].Payload;

        // State should be identical
        Assert.Equal(weatherStateCreate.Location, weatherStateUnknown.Location);
        Assert.Equal(weatherStateCreate.TemperatureC, weatherStateUnknown.TemperatureC);
        Assert.Equal(weatherStateCreate.Summary, weatherStateUnknown.Summary);
    }

    private Event CreateEvent(IEventPayload payload, DateTime timestamp)
    {
        var sortableId = SortableUniqueId.Generate(timestamp, Guid.NewGuid());
        return new Event(
            payload,
            sortableId,
            payload.GetType().Name,
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "TestUser"),
            new List<string>());
    }

    // Test event type that's not handled by WeatherForecastProjector
    public record UnknownEventType(string Data) : IEventPayload;

    // Test tag for non-weather events
    public record TestTag(string Value) : ITag
    {
        public string GetTagGroup() => "Test";
        public string GetTagContent() => Value;
        public bool IsConsistencyTag() => false;
        public string GetTag() => $"Test:{Value}";
    }
}
