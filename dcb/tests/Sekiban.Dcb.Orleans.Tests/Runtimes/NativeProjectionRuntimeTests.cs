using Dcb.Domain;
using Dcb.Domain.Projections;
using Dcb.Domain.Queries;
using Dcb.Domain.Weather;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Runtime;
using Sekiban.Dcb.Runtime.Native;
using Sekiban.Dcb.Tests;
using Xunit;

namespace Sekiban.Dcb.Orleans.Tests.Runtimes;

public class NativeProjectionRuntimeTests
{
    private readonly DcbDomainTypes _domainTypes = DomainType.GetDomainTypes();
    private readonly NativeProjectionRuntime _runtime;

    public NativeProjectionRuntimeTests()
    {
        _runtime = new NativeProjectionRuntime(_domainTypes);
    }

    [Fact]
    public void GetAllProjectorNames_should_return_registered_projectors()
    {
        // When
        var names = _runtime.GetAllProjectorNames();

        // Then
        Assert.NotEmpty(names);
        Assert.Contains("WeatherForecastProjection", names);
    }

    [Fact]
    public void GetProjectorVersion_should_return_version_for_known_projector()
    {
        // When
        var result = _runtime.GetProjectorVersion("WeatherForecastProjection");

        // Then
        Assert.True(result.IsSuccess);
        Assert.Equal("1.0.0", result.GetValue());
    }

    [Fact]
    public void GetProjectorVersion_should_fail_for_unknown_projector()
    {
        // When
        var result = _runtime.GetProjectorVersion("NonExistentProjector");

        // Then
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void GenerateInitialState_should_create_empty_state()
    {
        // When
        var result = _runtime.GenerateInitialState("WeatherForecastProjection");

        // Then
        Assert.True(result.IsSuccess);
        var state = result.GetValue();
        Assert.Equal(0, state.SafeVersion);
        Assert.Equal(0, state.UnsafeVersion);
        Assert.Null(state.SafeLastSortableUniqueId);
    }

    [Fact]
    public void GenerateInitialState_should_fail_for_unknown_projector()
    {
        // When
        var result = _runtime.GenerateInitialState("NonExistentProjector");

        // Then
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void ApplyEvent_should_update_state()
    {
        // Given
        var initialState = _runtime.GenerateInitialState("WeatherForecastProjection");
        Assert.True(initialState.IsSuccess);

        var forecastId = Guid.NewGuid();
        var payload = new WeatherForecastCreated(
            forecastId, "Tokyo", new DateOnly(2026, 1, 1), 25, "Sunny");
        var tag = new WeatherForecastTag(forecastId);
        var ev = EventTestHelper.CreateEvent(payload, tag);

        var threshold = SortableUniqueId.GetSafeIdFromUtc();

        // When
        var result = _runtime.ApplyEvent(
            "WeatherForecastProjection",
            initialState.GetValue(),
            ev,
            threshold);

        // Then
        Assert.True(result.IsSuccess);
        var state = result.GetValue();
        Assert.NotNull(state.LastSortableUniqueId);
    }

    [Fact]
    public void ApplyEvents_should_apply_multiple_events()
    {
        // Given
        var initialState = _runtime.GenerateInitialState("WeatherForecastProjection");
        Assert.True(initialState.IsSuccess);

        var forecastId1 = Guid.NewGuid();
        var forecastId2 = Guid.NewGuid();
        var threshold = SortableUniqueId.GetSafeIdFromUtc();

        var events = new[]
        {
            EventTestHelper.CreateEvent(
                new WeatherForecastCreated(forecastId1, "Tokyo", new DateOnly(2026, 1, 1), 25, "Sunny"),
                new WeatherForecastTag(forecastId1)),
            EventTestHelper.CreateEvent(
                new WeatherForecastCreated(forecastId2, "Osaka", new DateOnly(2026, 2, 1), 20, "Cloudy"),
                new WeatherForecastTag(forecastId2))
        };

        // When
        var result = _runtime.ApplyEvents(
            "WeatherForecastProjection",
            initialState.GetValue(),
            events,
            threshold);

        // Then
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ApplyEvents_should_fail_if_state_type_is_wrong()
    {
        // Given
        var wrongState = new FakeProjectionState();

        var ev = EventTestHelper.CreateEvent(
            new WeatherForecastCreated(
                Guid.NewGuid(), "Tokyo", new DateOnly(2026, 1, 1), 25, "Sunny"),
            new WeatherForecastTag(Guid.NewGuid()));

        // When
        var result = _runtime.ApplyEvent(
            "WeatherForecastProjection",
            wrongState,
            ev,
            SortableUniqueId.GetSafeIdFromUtc());

        // Then
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void SerializeState_and_DeserializeState_should_round_trip()
    {
        // Given
        var initialState = _runtime.GenerateInitialState("WeatherForecastProjection");
        Assert.True(initialState.IsSuccess);

        var forecastId = Guid.NewGuid();
        var ev = EventTestHelper.CreateEvent(
            new WeatherForecastCreated(forecastId, "Tokyo", new DateOnly(2026, 1, 1), 25, "Sunny"),
            new WeatherForecastTag(forecastId));

        var threshold = SortableUniqueId.GetSafeIdFromUtc();
        var afterApply = _runtime.ApplyEvent(
            "WeatherForecastProjection",
            initialState.GetValue(),
            ev,
            threshold);
        Assert.True(afterApply.IsSuccess);

        // When - Serialize
        var serialized = _runtime.SerializeState(
            "WeatherForecastProjection", afterApply.GetValue());
        Assert.True(serialized.IsSuccess);

        // When - Deserialize
        var deserialized = _runtime.DeserializeState(
            "WeatherForecastProjection",
            serialized.GetValue(),
            threshold);

        // Then
        Assert.True(deserialized.IsSuccess);
        var state = deserialized.GetValue();
        Assert.Equal(0, state.SafeVersion);
    }

    [Fact]
    public void ResolveProjectorName_should_find_projector_for_query()
    {
        // Given
        var query = new GetWeatherForecastCountQuery();

        // When
        var result = _runtime.ResolveProjectorName(query);

        // Then
        Assert.True(result.IsSuccess);
        Assert.Equal("WeatherForecastProjection", result.GetValue());
    }

    [Fact]
    public void ResolveProjectorName_should_find_projector_for_list_query()
    {
        // Given
        var listQuery = new GetWeatherForecastListQuery();

        // When
        var result = _runtime.ResolveProjectorName(listQuery);

        // Then
        Assert.True(result.IsSuccess);
        Assert.Equal("WeatherForecastProjection", result.GetValue());
    }

    [Fact]
    public async Task ExecuteQueryAsync_should_return_result()
    {
        // Given
        var initialState = _runtime.GenerateInitialState("WeatherForecastProjection");
        Assert.True(initialState.IsSuccess);

        var forecastId = Guid.NewGuid();
        var ev = EventTestHelper.CreateEvent(
            new WeatherForecastCreated(forecastId, "Tokyo", new DateOnly(2026, 1, 1), 25, "Sunny"),
            new WeatherForecastTag(forecastId));

        var threshold = SortableUniqueId.GetSafeIdFromUtc();
        var afterApply = _runtime.ApplyEvent(
            "WeatherForecastProjection",
            initialState.GetValue(),
            ev,
            threshold);
        Assert.True(afterApply.IsSuccess);

        var query = new GetWeatherForecastCountQuery();
        var queryParam = await SerializableQueryParameter.CreateFromAsync(
            query, _domainTypes.JsonSerializerOptions);

        var services = new ServiceCollection().BuildServiceProvider();

        // When
        var result = await _runtime.ExecuteQueryAsync(
            "WeatherForecastProjection",
            afterApply.GetValue(),
            queryParam,
            services);

        // Then
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExecuteListQueryAsync_should_return_result()
    {
        // Given
        var initialState = _runtime.GenerateInitialState("WeatherForecastProjection");
        Assert.True(initialState.IsSuccess);

        var forecastId = Guid.NewGuid();
        var ev = EventTestHelper.CreateEvent(
            new WeatherForecastCreated(forecastId, "Tokyo", new DateOnly(2026, 1, 1), 25, "Sunny"),
            new WeatherForecastTag(forecastId));

        var threshold = SortableUniqueId.GetSafeIdFromUtc();
        var afterApply = _runtime.ApplyEvent(
            "WeatherForecastProjection",
            initialState.GetValue(),
            ev,
            threshold);
        Assert.True(afterApply.IsSuccess);

        var listQuery = new GetWeatherForecastListQuery();
        var queryParam = await SerializableQueryParameter.CreateFromAsync(
            listQuery, _domainTypes.JsonSerializerOptions);

        var services = new ServiceCollection().BuildServiceProvider();

        // When
        var result = await _runtime.ExecuteListQueryAsync(
            "WeatherForecastProjection",
            afterApply.GetValue(),
            queryParam,
            services);

        // Then
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void NativeProjectionState_should_expose_payload_metadata()
    {
        // Given
        var initialState = _runtime.GenerateInitialState("WeatherForecastProjection");
        Assert.True(initialState.IsSuccess);
        var state = initialState.GetValue();

        // Then
        Assert.NotNull(state.GetSafePayload());
        Assert.NotNull(state.GetUnsafePayload());
        Assert.True(state.EstimatePayloadSizeBytes(null) > 0);
    }

    /// <summary>
    ///     Fake IProjectionState for testing type-check error paths.
    /// </summary>
    private class FakeProjectionState : IProjectionState
    {
        public int SafeVersion => 0;
        public int UnsafeVersion => 0;
        public string? SafeLastSortableUniqueId => null;
        public string? LastSortableUniqueId => null;
        public Guid? LastEventId => null;
        public object? GetSafePayload() => null;
        public object? GetUnsafePayload() => null;
        public long EstimatePayloadSizeBytes(System.Text.Json.JsonSerializerOptions? options) => 0;
    }
}
