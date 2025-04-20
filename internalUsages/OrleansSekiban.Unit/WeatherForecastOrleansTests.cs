using OrleansSekiban.Domain;
using OrleansSekiban.Domain.Aggregates.WeatherForecasts.Commands;
using OrleansSekiban.Domain.Aggregates.WeatherForecasts.Payloads;
using OrleansSekiban.Domain.Generated;
using OrleansSekiban.Domain.ValueObjects;
using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.Orleans.xUnit;
using Sekiban.Pure.Projectors;

namespace OrleansSekiban.Unit;

public class WeatherForecastOrleansTests : SekibanOrleansTestBase<WeatherForecastOrleansTests>
{
    public override SekibanDomainTypes GetDomainTypes() => 
        OrleansSekibanDomainDomainTypes.Generate(OrleansSekibanDomainEventsJsonContext.Default.Options);

    [Fact]
    public void OrleansTest_CreateAndQueryWeatherForecast() =>
        GivenCommandWithResult(new InputWeatherForecastCommand(
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
            .Conveyor(_ => ThenGetMultiProjectorWithResult<AggregateListProjector<WeatherForecastProjector>>())
            .Do(projector => 
            {
                var aggregates = projector.Aggregates.Values;
                Assert.Single(aggregates);
                var forecast = (WeatherForecast)aggregates.First().Payload;
                Assert.Equal("New Chicago", forecast.Location);
            })
            .UnwrapBox();
            
    [Fact]
    public void TestSerializable()
    {
        // Test that commands are serializable (important for Orleans)
        CheckSerializability(new InputWeatherForecastCommand(
            "Seattle", 
            new DateOnly(2025, 3, 4), 
            new TemperatureCelsius(15), 
            "Rainy"));
            
        CheckSerializability(new UpdateWeatherForecastLocationCommand(
            Guid.NewGuid(), 
            "Portland"));
            
        CheckSerializability(new DeleteWeatherForecastCommand(
            Guid.NewGuid()));
    }
}
