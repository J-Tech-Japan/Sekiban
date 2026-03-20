using Dcb.ImmutableModels.Events.Weather;
using Dcb.ImmutableModels.Tags;

namespace SekibanDcbOrleans.ImmutableModels.Unit.Events;

public class WeatherEventTests
{
    private readonly Guid _forecastId = Guid.CreateVersion7();

    [Fact]
    public void WeatherForecastCreated_GetEventWithTags_ReturnsWeatherForecastTag()
    {
        var evt = new WeatherForecastCreated(_forecastId, "Tokyo", new DateOnly(2026, 3, 20), 25, "Sunny");

        var result = evt.GetEventWithTags();

        var tag = Assert.Single(result.Tags);
        var weatherTag = Assert.IsType<WeatherForecastTag>(tag);
        Assert.Equal(_forecastId, weatherTag.ForecastId);
    }

    [Fact]
    public void WeatherForecastUpdated_GetEventWithTags_ReturnsWeatherForecastTag()
    {
        var evt = new WeatherForecastUpdated(_forecastId, "Osaka", new DateOnly(2026, 3, 21), 20, "Cloudy");

        var result = evt.GetEventWithTags();

        var tag = Assert.Single(result.Tags);
        Assert.IsType<WeatherForecastTag>(tag);
    }

    [Fact]
    public void WeatherForecastDeleted_GetEventWithTags_ReturnsWeatherForecastTag()
    {
        var evt = new WeatherForecastDeleted(_forecastId);

        var result = evt.GetEventWithTags();

        var tag = Assert.Single(result.Tags);
        Assert.IsType<WeatherForecastTag>(tag);
    }

    [Fact]
    public void LocationNameChanged_GetEventWithTags_ReturnsWeatherForecastTag()
    {
        var evt = new LocationNameChanged(_forecastId, "Osaka", "Tokyo");

        var result = evt.GetEventWithTags();

        var tag = Assert.Single(result.Tags);
        Assert.IsType<WeatherForecastTag>(tag);
    }
}
