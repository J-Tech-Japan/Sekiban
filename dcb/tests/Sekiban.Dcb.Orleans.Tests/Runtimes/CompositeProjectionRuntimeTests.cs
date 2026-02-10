using Dcb.Domain;
using Dcb.Domain.Weather;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Runtime.Native;
using Sekiban.Dcb.Tests;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests.Runtimes;

public class CompositeProjectionRuntimeTests
{
    private readonly DcbDomainTypes _domainTypes = DomainType.GetDomainTypes();

    [Fact]
    public void GetAllProjectorNames_should_aggregate_from_all_runtimes()
    {
        // Given
        var nativeRuntime = new NativeProjectionRuntime(_domainTypes);
        var resolver = new ProjectorRuntimeResolver(nativeRuntime);
        var composite = new CompositeProjectionRuntime(resolver);

        // When
        var names = composite.GetAllProjectorNames();

        // Then
        Assert.NotEmpty(names);
        Assert.Contains("WeatherForecastProjection", names);
    }

    [Fact]
    public void GenerateInitialState_should_delegate_to_resolved_runtime()
    {
        // Given
        var nativeRuntime = new NativeProjectionRuntime(_domainTypes);
        var resolver = new ProjectorRuntimeResolver(nativeRuntime);
        var composite = new CompositeProjectionRuntime(resolver);

        // When
        var result = composite.GenerateInitialState("WeatherForecastProjection");

        // Then
        Assert.True(result.IsSuccess);
        var state = result.GetValue();
        Assert.Equal(0, state.SafeVersion);
        Assert.Equal(0, state.UnsafeVersion);
    }

    [Fact]
    public void ApplyEvent_should_delegate_to_resolved_runtime()
    {
        // Given
        var nativeRuntime = new NativeProjectionRuntime(_domainTypes);
        var resolver = new ProjectorRuntimeResolver(nativeRuntime);
        var composite = new CompositeProjectionRuntime(resolver);

        var initialState = composite.GenerateInitialState("WeatherForecastProjection");
        Assert.True(initialState.IsSuccess);

        var forecastId = Guid.NewGuid();
        var payload = new WeatherForecastCreated(
            forecastId, "Tokyo", new DateOnly(2026, 1, 1), 25, "Sunny");
        var tag = new WeatherForecastTag(forecastId);
        var ev = EventTestHelper.CreateEvent(payload, tag);

        var threshold = SortableUniqueId.GetSafeIdFromUtc();

        // When
        var result = composite.ApplyEvent(
            "WeatherForecastProjection",
            initialState.GetValue(),
            ev,
            threshold);

        // Then
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void GetProjectorVersion_should_delegate()
    {
        // Given
        var nativeRuntime = new NativeProjectionRuntime(_domainTypes);
        var resolver = new ProjectorRuntimeResolver(nativeRuntime);
        var composite = new CompositeProjectionRuntime(resolver);

        // When
        var result = composite.GetProjectorVersion("WeatherForecastProjection");

        // Then
        Assert.True(result.IsSuccess);
        Assert.Equal("1.0.0", result.GetValue());
    }
}
