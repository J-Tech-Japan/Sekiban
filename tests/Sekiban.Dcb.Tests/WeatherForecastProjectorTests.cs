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

public class WeatherForecastProjectorTests
{
    private readonly WeatherForecastProjection _projector;
    private readonly DcbDomainTypes _domainTypes;

    public WeatherForecastProjectorTests()
    {
        _projector = WeatherForecastProjection.GenerateInitialPayload();
        
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
    public void Projector_OnlyProcessesEventsWithWeatherForecastTag()
    {
        // Arrange
        var forecastId = Guid.NewGuid();
        var weatherTag = new WeatherForecastTag(forecastId);
        var otherTag = new TestTag("other");

        var eventWithWeatherTag = CreateEvent(
            new WeatherForecastCreated(forecastId, "Location1", DateOnly.FromDateTime(DateTime.UtcNow), 25, "Sunny"),
            DateTime.UtcNow.AddSeconds(-30) // Safe event
        );

        var eventWithoutWeatherTag = CreateEvent(
            new WeatherForecastCreated(
                Guid.NewGuid(),
                "Location2",
                DateOnly.FromDateTime(DateTime.UtcNow),
                20,
                "Cloudy"),
            DateTime.UtcNow.AddSeconds(-30) // Safe event
        );

        // Act
        var result1 = WeatherForecastProjection.Project(_projector, eventWithWeatherTag, new List<ITag> { weatherTag }, _domainTypes);
        var projectorAfterTag = result1.GetValue();

        var result2 = WeatherForecastProjection.Project(
            projectorAfterTag,
            eventWithoutWeatherTag,
            new List<ITag> { otherTag },
            _domainTypes);
        var projectorAfterNoTag = result2.GetValue();

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);

        // Should have one forecast (from event with WeatherForecastTag)
        var forecasts = projectorAfterTag.GetCurrentForecasts();
        Assert.Single(forecasts);
        Assert.Contains(forecastId, forecasts.Keys);

        // Should still have one forecast (event without tag was skipped)
        var forecastsAfterNoTag = projectorAfterNoTag.GetCurrentForecasts();
        Assert.Single(forecastsAfterNoTag);
    }

    [Fact]
    public void Projector_HandlesUnsafeAndSafeEventsCorrectly()
    {
        // Arrange
        var forecastId = Guid.NewGuid();
        var tag = new WeatherForecastTag(forecastId);

        // Safe event (older than SafeWindow)
        var safeEvent = CreateEvent(
            new WeatherForecastCreated(
                forecastId,
                "Location",
                DateOnly.FromDateTime(DateTime.UtcNow),
                20,
                "Safe Weather"),
            DateTime.UtcNow.AddSeconds(-30));

        // Unsafe event (within SafeWindow)
        var unsafeEvent = CreateEvent(
            new WeatherForecastUpdated(
                forecastId,
                "Location",
                DateOnly.FromDateTime(DateTime.UtcNow),
                25,
                "Unsafe Weather"),
            DateTime.UtcNow.AddSeconds(-5));

        // Act
        var result1 = WeatherForecastProjection.Project(_projector, safeEvent, new List<ITag> { tag }, _domainTypes);
        var afterSafe = result1.GetValue();

        var result2 = WeatherForecastProjection.Project(afterSafe, unsafeEvent, new List<ITag> { tag }, _domainTypes);
        var afterUnsafe = result2.GetValue();

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);

        // Check if forecast is marked as unsafe
        Assert.True(afterUnsafe.IsForecastUnsafe(forecastId));

        // Current state should have unsafe modifications
        var currentForecasts = afterUnsafe.GetCurrentForecasts();
        Assert.Single(currentForecasts);
        var currentForecast = currentForecasts[forecastId];
        Assert.Equal(25, currentForecast.TemperatureC);
        Assert.Equal("Unsafe Weather", currentForecast.Summary);

        // Safe state should have original values
        var safeForecasts = afterUnsafe.GetSafeForecasts();
        Assert.Single(safeForecasts);
        var safeForecast = safeForecasts[forecastId];
        Assert.Equal(20, safeForecast.TemperatureC);
        Assert.Equal("Safe Weather", safeForecast.Summary);
    }

    [Fact]
    public void Projector_ProcessesMultipleTagsOnSameEvent()
    {
        // Arrange
        var forecastId1 = Guid.NewGuid();
        var forecastId2 = Guid.NewGuid();
        var tag1 = new WeatherForecastTag(forecastId1);
        var tag2 = new WeatherForecastTag(forecastId2);

        var createEvent = CreateEvent(
            new WeatherForecastCreated(
                forecastId1,
                "Location",
                DateOnly.FromDateTime(DateTime.UtcNow),
                22,
                "Multiple Forecasts"),
            DateTime.UtcNow.AddSeconds(-30));

        // Act - Process event with multiple tags
        var result = WeatherForecastProjection.Project(_projector, createEvent, new List<ITag> { tag1, tag2 }, _domainTypes);
        var afterMultipleTags = result.GetValue();

        // Assert
        Assert.True(result.IsSuccess);

        var forecasts = afterMultipleTags.GetCurrentForecasts();
        Assert.Equal(2, forecasts.Count);
        Assert.Contains(forecastId1, forecasts.Keys);
        Assert.Contains(forecastId2, forecasts.Keys);

        // Both forecasts should have the same data
        Assert.Equal(22, forecasts[forecastId1].TemperatureC);
        Assert.Equal(22, forecasts[forecastId2].TemperatureC);
        Assert.Equal("Multiple Forecasts", forecasts[forecastId1].Summary);
        Assert.Equal("Multiple Forecasts", forecasts[forecastId2].Summary);
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
                "Location",
                DateOnly.FromDateTime(DateTime.UtcNow),
                20,
                "To Be Deleted"),
            DateTime.UtcNow.AddSeconds(-30));

        var deleteEvent = CreateEvent(new WeatherForecastDeleted(forecastId), DateTime.UtcNow.AddSeconds(-25));

        // Act
        var result1 = WeatherForecastProjection.Project(_projector, createEvent, new List<ITag> { tag }, _domainTypes);
        var afterCreate = result1.GetValue();

        var result2 = WeatherForecastProjection.Project(afterCreate, deleteEvent, new List<ITag> { tag }, _domainTypes);
        var afterDelete = result2.GetValue();

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);

        // Should have forecast after create
        var forecastsAfterCreate = afterCreate.GetCurrentForecasts();
        Assert.Single(forecastsAfterCreate);

        // Should be empty after delete
        var forecastsAfterDelete = afterDelete.GetCurrentForecasts();
        Assert.Empty(forecastsAfterDelete);
    }

    private Event CreateEvent(IEventPayload payload, DateTime timestamp)
    {
        var sortableId = SortableUniqueId.Generate(timestamp, Guid.NewGuid());
        return new Event(
            payload,
            sortableId,
            payload.GetType().Name,
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "Test"),
            new List<string>());
    }

    // Test tag for comparison
    private record TestTag(string Content) : ITag
    {
        public bool IsConsistencyTag() => false;
        public string GetTagGroup() => "Test";
        public string GetTagContent() => Content;
    }
}
