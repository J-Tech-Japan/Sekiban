using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.xUnit;
using SharedDomain;
using SharedDomain.Aggregates.WeatherForecasts;
using SharedDomain.Aggregates.WeatherForecasts.Commands;
using SharedDomain.Aggregates.WeatherForecasts.Payloads;
using SharedDomain.Aggregates.WeatherForecasts.Queries;
using SharedDomain.Generated;
using SharedDomain.ValueObjects;
namespace OrleansSekiban.Unit;

public class WeatherForecastTests : SekibanInMemoryTestBase
{
    protected override SekibanDomainTypes GetDomainTypes() =>
        SharedDomainDomainTypes.Generate(SharedDomainEventsJsonContext.Default.Options);

    [Fact]
    public void InputWeatherForecast_CreatesCorrectAggregate()
    {
        // Given - Execute a command to create a weather forecast
        var location = "Seattle";
        var date = new DateOnly(2025, 3, 4);
        var temperatureC = new TemperatureCelsius(25);
        var summary = "Warm";

        var response = GivenCommand(new InputWeatherForecastCommand(location, date, temperatureC, summary));

        // Verify the command was successful
        Assert.Equal(1, response.Version);

        // Then - Get the aggregate and verify its state
        var aggregate = ThenGetAggregate<WeatherForecastProjector>(response.PartitionKeys);
        Assert.IsType<WeatherForecast>(aggregate.Payload);

        var forecast = (WeatherForecast)aggregate.Payload;
        Assert.Equal(location, forecast.Location);
        Assert.Equal(date, forecast.Date);
        Assert.Equal(temperatureC, forecast.TemperatureC);
        Assert.Equal(summary, forecast.Summary);
    }

    [Fact]
    public void UpdateWeatherForecastLocation_ChangesLocation()
    {
        // Given - Create a weather forecast
        var initialLocation = "Portland";
        var date = new DateOnly(2025, 3, 4);
        var temperatureC = new TemperatureCelsius(20);
        var summary = "Mild";

        var createResponse
            = GivenCommand(new InputWeatherForecastCommand(initialLocation, date, temperatureC, summary));

        // When - Update the location
        var newLocation = "Vancouver";
        var updateResponse = WhenCommand(
            new UpdateWeatherForecastLocationCommand(createResponse.PartitionKeys.AggregateId, newLocation));

        // Then - Verify the location was updated
        var aggregate = ThenGetAggregate<WeatherForecastProjector>(updateResponse.PartitionKeys);
        Assert.IsType<WeatherForecast>(aggregate.Payload);

        var forecast = (WeatherForecast)aggregate.Payload;
        Assert.Equal(newLocation, forecast.Location);
        Assert.Equal(date, forecast.Date);
        Assert.Equal(temperatureC, forecast.TemperatureC);
        Assert.Equal(summary, forecast.Summary);
    }

    [Fact]
    public void DeleteWeatherForecast_ChangesStateToDeleted()
    {
        // Given - Create a weather forecast
        var location = "San Francisco";
        var date = new DateOnly(2025, 3, 4);
        var temperatureC = new TemperatureCelsius(18);
        var summary = "Foggy";

        var createResponse = GivenCommand(new InputWeatherForecastCommand(location, date, temperatureC, summary));

        // When - Delete the forecast
        var deleteResponse = WhenCommand(new DeleteWeatherForecastCommand(createResponse.PartitionKeys.AggregateId));

        // Then - Verify the forecast was deleted
        var aggregate = ThenGetAggregate<WeatherForecastProjector>(deleteResponse.PartitionKeys);
        Assert.IsType<DeletedWeatherForecast>(aggregate.Payload);
    }

    [Fact]
    public void WeatherForecastQuery_ReturnsCorrectResults()
    {
        // Given - Create multiple weather forecasts
        var seattle = GivenCommand(
            new InputWeatherForecastCommand("Seattle", new DateOnly(2025, 3, 4), new TemperatureCelsius(15), "Rainy"));

        var portland = GivenCommand(
            new InputWeatherForecastCommand(
                "Portland",
                new DateOnly(2025, 3, 5),
                new TemperatureCelsius(18),
                "Cloudy"));

        var sanFrancisco = GivenCommand(
            new InputWeatherForecastCommand(
                "San Francisco",
                new DateOnly(2025, 3, 6),
                new TemperatureCelsius(22),
                "Sunny"));

        // When - Query for all forecasts
        var queryResult = ThenQuery(new WeatherForecastQuery());

        // Then - Verify all three forecasts are returned
        Assert.Equal(3, queryResult.Items.Count());
        var sanFranciscoResult = queryResult.Items.FirstOrDefault(f => f.Location == "San Francisco");
        Assert.NotNull(sanFranciscoResult);
        Assert.Equal(sanFrancisco.PartitionKeys.AggregateId, sanFranciscoResult.WeatherForecastId);
        Assert.Equal("San Francisco", sanFranciscoResult.Location);
        Assert.Equal(new DateOnly(2025, 3, 6), sanFranciscoResult.Date);
        Assert.Equal(22, sanFranciscoResult.TemperatureC);
        Assert.Equal("Sunny", sanFranciscoResult.Summary);
    }

    [Fact]
    public void ChainedTest_CreateUpdateAndQueryWeatherForecast() =>
        GivenCommandWithResult(
                new InputWeatherForecastCommand(
                    "Chicago",
                    new DateOnly(2025, 3, 7),
                    new TemperatureCelsius(10),
                    "Windy"))
            .Do(response => Assert.Equal(1, response.Version))
            .Conveyor(response => WhenCommandWithResult(
                new UpdateWeatherForecastLocationCommand(response.PartitionKeys.AggregateId, "New Chicago")))
            .Do(response => Assert.Equal(2, response.Version))
            .Conveyor(response => ThenGetAggregateWithResult<WeatherForecastProjector>(response.PartitionKeys))
            .Conveyor(aggregate => aggregate.Payload.ToResultBox().Cast<WeatherForecast>())
            .Do(forecast =>
            {
                Assert.Equal("New Chicago", forecast.Location);
                Assert.Equal(new DateOnly(2025, 3, 7), forecast.Date);
                Assert.Equal(new TemperatureCelsius(10), forecast.TemperatureC);
                Assert.Equal("Windy", forecast.Summary);
            })
            .UnwrapBox();
}
