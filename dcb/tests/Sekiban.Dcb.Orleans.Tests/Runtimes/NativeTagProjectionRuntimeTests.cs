using Dcb.Domain;
using Dcb.Domain.Weather;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Runtime.Native;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Tests;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests.Runtimes;

public class NativeTagProjectionRuntimeTests
{
    private readonly DcbDomainTypes _domainTypes = DomainType.GetDomainTypes();
    private readonly NativeTagProjectionRuntime _runtime;

    public NativeTagProjectionRuntimeTests()
    {
        _runtime = new NativeTagProjectionRuntime(_domainTypes);
    }

    [Fact]
    public void GetProjector_should_return_projector_for_known_name()
    {
        // When
        var result = _runtime.GetProjector(nameof(WeatherForecastProjector));

        // Then
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void GetProjector_should_fail_for_unknown_name()
    {
        // When
        var result = _runtime.GetProjector("NonExistentProjector");

        // Then
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void GetProjectorVersion_should_return_version()
    {
        // When
        var result = _runtime.GetProjectorVersion(nameof(WeatherForecastProjector));

        // Then
        Assert.True(result.IsSuccess);
        Assert.Equal("1.0.0", result.GetValue());
    }

    [Fact]
    public void GetAllProjectorNames_should_include_known_projectors()
    {
        // When
        var names = _runtime.GetAllProjectorNames();

        // Then
        Assert.Contains(nameof(WeatherForecastProjector), names);
    }

    [Fact]
    public void TryGetProjectorForTagGroup_should_return_projector_name()
    {
        // When
        var projectorName = _runtime.TryGetProjectorForTagGroup("WeatherForecast");

        // Then
        Assert.NotNull(projectorName);
        Assert.Equal(nameof(WeatherForecastProjector), projectorName);
    }

    [Fact]
    public void TryGetProjectorForTagGroup_should_return_null_for_unknown_group()
    {
        // When
        var projectorName = _runtime.TryGetProjectorForTagGroup("NonExistentGroup");

        // Then
        Assert.Null(projectorName);
    }

    [Fact]
    public void ResolveTag_should_return_tag_for_valid_string()
    {
        // Given
        var forecastId = Guid.NewGuid();
        var tagString = $"WeatherForecast:{forecastId}";

        // When
        var tag = _runtime.ResolveTag(tagString);

        // Then
        Assert.NotNull(tag);
        Assert.Equal("WeatherForecast", tag.GetTagGroup());
    }

    [Fact]
    public void Projector_Apply_should_project_event()
    {
        // Given
        var projectorResult = _runtime.GetProjector(nameof(WeatherForecastProjector));
        Assert.True(projectorResult.IsSuccess);
        var projector = projectorResult.GetValue();

        var forecastId = Guid.NewGuid();
        var payload = new WeatherForecastCreated(
            forecastId, "Tokyo", new DateOnly(2026, 1, 1), 25, "Sunny");
        var tag = new WeatherForecastTag(forecastId);
        var ev = EventTestHelper.CreateEvent(payload, tag);

        // When
        var state = projector.Apply(null, ev);

        // Then
        Assert.NotNull(state);
        var typed = Assert.IsType<WeatherForecastState>(state);
        Assert.Equal(forecastId, typed.ForecastId);
        Assert.Equal("Tokyo", typed.Location);
        Assert.Equal(25, typed.TemperatureC);
    }

    [Fact]
    public void SerializePayload_and_DeserializePayload_should_round_trip()
    {
        // Given
        var state = new WeatherForecastState
        {
            ForecastId = Guid.NewGuid(),
            Location = "Tokyo",
            Date = new DateOnly(2026, 1, 1),
            TemperatureC = 25,
            Summary = "Sunny"
        };

        // When
        var serializeResult = _runtime.SerializePayload(state);
        Assert.True(serializeResult.IsSuccess);

        var deserializeResult = _runtime.DeserializePayload(
            nameof(WeatherForecastState), serializeResult.GetValue());

        // Then
        Assert.True(deserializeResult.IsSuccess);
        var deserialized = Assert.IsType<WeatherForecastState>(deserializeResult.GetValue());
        Assert.Equal(state.ForecastId, deserialized.ForecastId);
        Assert.Equal(state.Location, deserialized.Location);
    }
}
