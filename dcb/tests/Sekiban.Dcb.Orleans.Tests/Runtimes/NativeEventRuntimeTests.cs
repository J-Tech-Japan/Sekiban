using Dcb.Domain;
using Dcb.Domain.Weather;
using Sekiban.Dcb.Runtime.Native;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests.Runtimes;

public class NativeEventRuntimeTests
{
    private readonly DcbDomainTypes _domainTypes = DomainType.GetDomainTypes();
    private readonly NativeEventRuntime _runtime;

    public NativeEventRuntimeTests()
    {
        _runtime = new NativeEventRuntime(_domainTypes);
    }

    [Fact]
    public void SerializeEventPayload_should_produce_valid_json()
    {
        // Given
        var payload = new WeatherForecastCreated(
            Guid.NewGuid(), "Tokyo", new DateOnly(2026, 1, 1), 25, "Sunny");

        // When
        var json = _runtime.SerializeEventPayload(payload);

        // Then
        Assert.NotNull(json);
        Assert.NotEmpty(json);
        Assert.Contains("Tokyo", json);
    }

    [Fact]
    public void DeserializeEventPayload_should_reconstruct_payload()
    {
        // Given
        var original = new WeatherForecastCreated(
            Guid.NewGuid(), "Osaka", new DateOnly(2026, 6, 15), 30, "Hot");
        var json = _runtime.SerializeEventPayload(original);

        // When
        var deserialized = _runtime.DeserializeEventPayload(
            nameof(WeatherForecastCreated), json);

        // Then
        Assert.NotNull(deserialized);
        var typed = Assert.IsType<WeatherForecastCreated>(deserialized);
        Assert.Equal(original.ForecastId, typed.ForecastId);
        Assert.Equal(original.Location, typed.Location);
        Assert.Equal(original.TemperatureC, typed.TemperatureC);
    }

    [Fact]
    public void DeserializeEventPayload_should_return_null_for_unknown_type()
    {
        // Given
        var json = """{"foo":"bar"}""";

        // When
        var result = _runtime.DeserializeEventPayload("NonExistentEvent", json);

        // Then
        Assert.Null(result);
    }

    [Fact]
    public void GetEventType_should_return_type_for_known_event()
    {
        // When
        var type = _runtime.GetEventType(nameof(WeatherForecastCreated));

        // Then
        Assert.NotNull(type);
        Assert.Equal(typeof(WeatherForecastCreated), type);
    }

    [Fact]
    public void GetEventType_should_return_null_for_unknown_event()
    {
        // When
        var type = _runtime.GetEventType("NonExistentEvent");

        // Then
        Assert.Null(type);
    }
}
