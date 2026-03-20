using Dcb.ImmutableModels.Events.Weather;
using Dcb.ImmutableModels.States.Weather;
using Dcb.ImmutableModels.States.Weather.Deciders;

namespace SekibanDcbOrleans.ImmutableModels.Unit.States;

public class WeatherForecastDeciderTests
{
    private readonly Guid _forecastId = Guid.CreateVersion7();

    [Fact]
    public void WeatherForecastCreatedDecider_Create_ReturnsState()
    {
        var created = new WeatherForecastCreated(_forecastId, "Tokyo", new DateOnly(2026, 3, 20), 25, "Sunny");

        var state = WeatherForecastCreatedDecider.Create(created);

        Assert.Equal(_forecastId, state.ForecastId);
        Assert.Equal("Tokyo", state.Location);
        Assert.Equal(new DateOnly(2026, 3, 20), state.Date);
        Assert.Equal(25, state.TemperatureC);
        Assert.Equal("Sunny", state.Summary);
        Assert.False(state.IsDeleted);
    }

    [Fact]
    public void WeatherForecastUpdatedDecider_Evolve_UpdatesAllFields()
    {
        var state = new WeatherForecastState(_forecastId, "Tokyo", new DateOnly(2026, 3, 20), 25, "Sunny");
        var updated = new WeatherForecastUpdated(_forecastId, "Osaka", new DateOnly(2026, 3, 21), 20, "Cloudy");

        var newState = WeatherForecastUpdatedDecider.Evolve(state, updated);

        Assert.Equal("Osaka", newState.Location);
        Assert.Equal(new DateOnly(2026, 3, 21), newState.Date);
        Assert.Equal(20, newState.TemperatureC);
        Assert.Equal("Cloudy", newState.Summary);
    }

    [Fact]
    public void WeatherForecastUpdatedDecider_Validate_ThrowsWhenDeleted()
    {
        var state = new WeatherForecastState(_forecastId, "Tokyo", new DateOnly(2026, 3, 20), 25, "Sunny", IsDeleted: true);

        Assert.Throws<InvalidOperationException>(() => WeatherForecastUpdatedDecider.Validate(state));
    }

    [Fact]
    public void WeatherForecastDeletedDecider_Evolve_SetsIsDeletedTrue()
    {
        var state = new WeatherForecastState(_forecastId, "Tokyo", new DateOnly(2026, 3, 20), 25, "Sunny");
        var deleted = new WeatherForecastDeleted(_forecastId);

        var newState = WeatherForecastDeletedDecider.Evolve(state, deleted);

        Assert.True(newState.IsDeleted);
    }

    [Fact]
    public void WeatherForecastDeletedDecider_Validate_ThrowsWhenAlreadyDeleted()
    {
        var state = new WeatherForecastState(_forecastId, "Tokyo", new DateOnly(2026, 3, 20), 25, "Sunny", IsDeleted: true);

        Assert.Throws<InvalidOperationException>(() => WeatherForecastDeletedDecider.Validate(state));
    }

    [Fact]
    public void LocationNameChangedDecider_Evolve_UpdatesLocation()
    {
        var state = new WeatherForecastState(_forecastId, "Tokyo", new DateOnly(2026, 3, 20), 25, "Sunny");
        var changed = new LocationNameChanged(_forecastId, "Osaka", "Tokyo");

        var newState = LocationNameChangedDecider.Evolve(state, changed);

        Assert.Equal("Osaka", newState.Location);
    }

    [Fact]
    public void LocationNameChangedDecider_Validate_ThrowsWhenDeleted()
    {
        var state = new WeatherForecastState(_forecastId, "Tokyo", new DateOnly(2026, 3, 20), 25, "Sunny", IsDeleted: true);

        Assert.Throws<InvalidOperationException>(() => LocationNameChangedDecider.Validate(state, "Osaka"));
    }

    [Fact]
    public void LocationNameChangedDecider_Validate_ThrowsWhenSameName()
    {
        var state = new WeatherForecastState(_forecastId, "Tokyo", new DateOnly(2026, 3, 20), 25, "Sunny");

        Assert.Throws<InvalidOperationException>(() => LocationNameChangedDecider.Validate(state, "Tokyo"));
    }

    [Fact]
    public void WeatherForecastState_Empty_HasDefaultValues()
    {
        var empty = WeatherForecastState.Empty;

        Assert.Equal(Guid.Empty, empty.ForecastId);
        Assert.Equal(string.Empty, empty.Location);
        Assert.Equal(DateOnly.MinValue, empty.Date);
        Assert.Equal(0, empty.TemperatureC);
        Assert.Null(empty.Summary);
        Assert.False(empty.IsDeleted);
    }
}
