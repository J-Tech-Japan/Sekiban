using Dcb.Domain;
using Dcb.Domain.Weather;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Tests;

/// <summary>
///     Tests for Weather domain commands using GeneralSekibanExecutor
///     Testing create, change location name, and get operations
/// </summary>
public class WeatherForecastCommandTests
{
    private readonly InMemoryObjectAccessor _actorAccessor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly InMemoryEventStore _eventStore;
    private readonly GeneralSekibanExecutor _executor;

    public WeatherForecastCommandTests()
    {
        _eventStore = new InMemoryEventStore();
        _domainTypes = DomainType.GetDomainTypes();
        _actorAccessor = new InMemoryObjectAccessor(_eventStore, _domainTypes);
        _executor = new GeneralSekibanExecutor(_eventStore, _actorAccessor, _domainTypes);
    }

    [Fact]
    public async Task Should_Create_WeatherForecast_Successfully()
    {
        // Arrange
        var forecastId = Guid.NewGuid();
        var command = new CreateWeatherForecast
        {
            ForecastId = forecastId,
            Location = "Tokyo",
            Date = new DateOnly(2024, 12, 25),
            TemperatureC = 10,
            Summary = "Mild winter day"
        };

        // Act
        var result = await _executor.ExecuteAsync(command);

        // Assert
        Assert.True(result.IsSuccess);
        var executionResult = result.GetValue();
        Assert.NotEqual(Guid.Empty, executionResult.EventId);
        Assert.Single(executionResult.TagWrites);
        Assert.Equal($"WeatherForecast:{forecastId}", executionResult.TagWrites[0].Tag);

        // Verify the forecast was created
        var forecastTag = new WeatherForecastTag(forecastId);
        var tagExists = await _eventStore.TagExistsAsync(forecastTag);
        Assert.True(tagExists.GetValue());

        // Verify the event
        var events = await _eventStore.ReadEventsByTagAsync(forecastTag);
        var eventsList = events.GetValue().ToList();
        Assert.Single(eventsList);
        var payload = eventsList[0].Payload as WeatherForecastCreated;
        Assert.NotNull(payload);
        Assert.Equal(forecastId, payload.ForecastId);
        Assert.Equal("Tokyo", payload.Location);
        Assert.Equal(new DateOnly(2024, 12, 25), payload.Date);
        Assert.Equal(10, payload.TemperatureC);
        Assert.Equal("Mild winter day", payload.Summary);
    }

    [Fact]
    public async Task Should_Change_Location_Name_Successfully()
    {
        // Arrange - First create a forecast
        var forecastId = Guid.NewGuid();
        var createCommand = new CreateWeatherForecast
        {
            ForecastId = forecastId,
            Location = "Tokyo",
            Date = new DateOnly(2024, 12, 25),
            TemperatureC = 10,
            Summary = "Mild winter day"
        };

        var createResult = await _executor.ExecuteAsync(createCommand);
        Assert.True(createResult.IsSuccess);

        // Act - Change the location name
        var changeCommand = new ChangeLocationName
        {
            ForecastId = forecastId,
            NewLocationName = "Tokyo, Japan"
        };

        var changeResult = await _executor.ExecuteAsync(changeCommand);

        // Assert
        Assert.True(changeResult.IsSuccess);
        var executionResult = changeResult.GetValue();
        Assert.NotEqual(Guid.Empty, executionResult.EventId);

        // Verify the event was written
        var forecastTag = new WeatherForecastTag(forecastId);
        var events = await _eventStore.ReadEventsByTagAsync(forecastTag);
        var eventsList = events.GetValue().ToList();
        Assert.Equal(2, eventsList.Count); // Create + Change events

        var changeEvent = eventsList[1].Payload as LocationNameChanged;
        Assert.NotNull(changeEvent);
        Assert.Equal(forecastId, changeEvent.ForecastId);
        Assert.Equal("Tokyo, Japan", changeEvent.NewLocationName);
        Assert.Equal("Tokyo", changeEvent.OldLocationName);
    }

    [Fact]
    public async Task Should_Get_Updated_State_After_Location_Change()
    {
        // Arrange - Create and update a forecast
        var forecastId = Guid.NewGuid();
        var createCommand = new CreateWeatherForecast
        {
            ForecastId = forecastId,
            Location = "Osaka",
            Date = new DateOnly(2024, 12, 26),
            TemperatureC = 8,
            Summary = "Cold day"
        };

        await _executor.ExecuteAsync(createCommand);

        var changeCommand = new ChangeLocationName
        {
            ForecastId = forecastId,
            NewLocationName = "Osaka, Japan"
        };

        await _executor.ExecuteAsync(changeCommand);

        // Act - Get the state
        var forecastTag = new WeatherForecastTag(forecastId);
        var tagStateId = new TagStateId(forecastTag, nameof(WeatherForecastProjector));
        var stateResult = await _executor.GetTagStateAsync(tagStateId);

        // Assert
        Assert.True(stateResult.IsSuccess);
        var state = stateResult.GetValue();
        Assert.NotNull(state);

        var payload = state.Payload as WeatherForecastState;
        Assert.NotNull(payload);
        Assert.Equal(forecastId, payload.ForecastId);
        Assert.Equal("Osaka, Japan", payload.Location); // Should have the updated location
        Assert.Equal(new DateOnly(2024, 12, 26), payload.Date);
        Assert.Equal(8, payload.TemperatureC);
        Assert.Equal("Cold day", payload.Summary);
        Assert.False(payload.IsDeleted);
    }

    [Fact]
    public async Task Should_Not_Change_Location_When_Same_Name()
    {
        // Arrange - Create a forecast
        var forecastId = Guid.NewGuid();
        var createCommand = new CreateWeatherForecast
        {
            ForecastId = forecastId,
            Location = "Kyoto",
            Date = new DateOnly(2024, 12, 27),
            TemperatureC = 5,
            Summary = "Chilly"
        };

        await _executor.ExecuteAsync(createCommand);

        // Act - Try to change to the same location name
        var changeCommand = new ChangeLocationName
        {
            ForecastId = forecastId,
            NewLocationName = "Kyoto" // Same as current
        };

        var changeResult = await _executor.ExecuteAsync(changeCommand);

        // Assert
        Assert.True(changeResult.IsSuccess);
        var executionResult = changeResult.GetValue();
        Assert.Equal(Guid.Empty, executionResult.EventId); // No event should be created

        // Verify only one event exists (the create event)
        var forecastTag = new WeatherForecastTag(forecastId);
        var events = await _eventStore.ReadEventsByTagAsync(forecastTag);
        var eventsList = events.GetValue().ToList();
        Assert.Single(eventsList);
    }

    [Fact]
    public async Task Should_Fail_To_Change_Location_For_NonExistent_Forecast()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();
        var changeCommand = new ChangeLocationName
        {
            ForecastId = nonExistentId,
            NewLocationName = "Nowhere"
        };

        // Act
        var result = await _executor.ExecuteAsync(changeCommand);

        // Assert
        Assert.False(result.IsSuccess);
        var exception = result.GetException();
        Assert.IsType<ApplicationException>(exception);
        Assert.Contains($"Weather forecast {nonExistentId} does not exist", exception.Message);
    }

    [Fact]
    public async Task Should_Fail_To_Change_Location_For_Deleted_Forecast()
    {
        // Arrange - Create and delete a forecast
        var forecastId = Guid.NewGuid();
        var createCommand = new CreateWeatherForecast
        {
            ForecastId = forecastId,
            Location = "Nagoya",
            Date = new DateOnly(2024, 12, 28),
            TemperatureC = 7,
            Summary = "Cloudy"
        };

        await _executor.ExecuteAsync(createCommand);

        var deleteCommand = new DeleteWeatherForecast
        {
            ForecastId = forecastId
        };

        await _executor.ExecuteAsync(deleteCommand);

        // Act - Try to change location of deleted forecast
        var changeCommand = new ChangeLocationName
        {
            ForecastId = forecastId,
            NewLocationName = "Nagoya, Japan"
        };

        var result = await _executor.ExecuteAsync(changeCommand);

        // Assert
        Assert.False(result.IsSuccess);
        var exception = result.GetException();
        Assert.IsType<ApplicationException>(exception);
        Assert.Contains($"Weather forecast {forecastId} has been deleted", exception.Message);
    }

    [Fact]
    public async Task Should_Get_Weather_Forecast_By_Id()
    {
        // Arrange - Create a forecast
        var forecastId = Guid.NewGuid();
        var createCommand = new CreateWeatherForecast
        {
            ForecastId = forecastId,
            Location = "Yokohama",
            Date = new DateOnly(2025, 1, 1),
            TemperatureC = 3,
            Summary = "New Year's Day"
        };

        await _executor.ExecuteAsync(createCommand);

        // Act - Get the forecast by ID
        var forecastTag = new WeatherForecastTag(forecastId);
        var tagStateId = new TagStateId(forecastTag, nameof(WeatherForecastProjector));
        var result = await _executor.GetTagStateAsync(tagStateId);

        // Assert
        Assert.True(result.IsSuccess);
        var state = result.GetValue();
        Assert.NotNull(state);
        Assert.Equal(1, state.Version); // Should have version 1 after create

        var payload = state.Payload as WeatherForecastState;
        Assert.NotNull(payload);
        Assert.Equal(forecastId, payload.ForecastId);
        Assert.Equal("Yokohama", payload.Location);
        Assert.Equal(new DateOnly(2025, 1, 1), payload.Date);
        Assert.Equal(3, payload.TemperatureC);
        Assert.Equal("New Year's Day", payload.Summary);
        Assert.False(payload.IsDeleted);
    }

    [Fact]
    public async Task Should_Update_And_Then_Change_Location_Successfully()
    {
        // Arrange - Create a forecast
        var forecastId = Guid.NewGuid();
        var createCommand = new CreateWeatherForecast
        {
            ForecastId = forecastId,
            Location = "Sapporo",
            Date = new DateOnly(2024, 12, 29),
            TemperatureC = -5,
            Summary = "Snowy"
        };

        await _executor.ExecuteAsync(createCommand);

        // Update the forecast (change temperature and summary)
        var updateCommand = new UpdateWeatherForecast
        {
            ForecastId = forecastId,
            Location = "Sapporo", // Keep same location in update
            Date = new DateOnly(2024, 12, 29),
            TemperatureC = -8,
            Summary = "Heavy snow"
        };

        await _executor.ExecuteAsync(updateCommand);

        // Act - Change the location name
        var changeCommand = new ChangeLocationName
        {
            ForecastId = forecastId,
            NewLocationName = "Sapporo, Hokkaido"
        };

        var changeResult = await _executor.ExecuteAsync(changeCommand);

        // Assert
        Assert.True(changeResult.IsSuccess);

        // Get final state
        var forecastTag = new WeatherForecastTag(forecastId);
        var tagStateId = new TagStateId(forecastTag, nameof(WeatherForecastProjector));
        var stateResult = await _executor.GetTagStateAsync(tagStateId);

        var state = stateResult.GetValue();
        var payload = state.Payload as WeatherForecastState;
        Assert.NotNull(payload);
        Assert.Equal("Sapporo, Hokkaido", payload.Location); // Updated location
        Assert.Equal(-8, payload.TemperatureC); // Updated temperature
        Assert.Equal("Heavy snow", payload.Summary); // Updated summary

        // Verify all events
        var events = await _eventStore.ReadEventsByTagAsync(forecastTag);
        var eventsList = events.GetValue().ToList();
        Assert.Equal(3, eventsList.Count); // Create + Update + ChangeLocation
    }
}
