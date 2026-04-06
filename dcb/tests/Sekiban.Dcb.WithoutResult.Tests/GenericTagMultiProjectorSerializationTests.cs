using Dcb.Domain.WithoutResult.Weather;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.WithoutResult.Tests;

public class GenericTagMultiProjectorSerializationTests
{
    private readonly DcbDomainTypes _domainTypes;

    public GenericTagMultiProjectorSerializationTests()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<WeatherForecastCreated>("WeatherForecastCreated");
        eventTypes.RegisterEventType<WeatherForecastDeleted>("WeatherForecastDeleted");

        var tagTypes = new SimpleTagTypes();
        tagTypes.RegisterTagGroupType<WeatherForecastTag>();

        var tagProjectorTypes = new SimpleTagProjectorTypes();
        tagProjectorTypes.RegisterProjector<WeatherForecastProjector>();

        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        tagStatePayloadTypes.RegisterPayloadType<WeatherForecastState>();

        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjectorWithCustomSerialization<
            GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>>();

        _domainTypes = new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            tagStatePayloadTypes,
            multiProjectorTypes,
            new SimpleQueryTypes());
    }

    [Fact]
    public void GenericTagProjector_SerializationRoundTrip_PreservesItemThatBecomesSafeAtSerializeTime()
    {
        var forecastId = Guid.NewGuid();
        var eventTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var processThreshold = SortableUniqueId.Generate(eventTime.AddSeconds(-20), Guid.Empty);
        var serializeThreshold = SortableUniqueId.Generate(eventTime.AddSeconds(20), Guid.Empty);
        var weatherEvent = CreateEvent(
            new WeatherForecastCreated(forecastId, "Tokyo", DateOnly.FromDateTime(eventTime), 20, "Sunny"),
            eventTime,
            forecastId) with { Tags = new List<string>() };

        var projected = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.Project(
            GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.GenerateInitialPayload(),
            weatherEvent,
            new List<ITag> { new WeatherForecastTag(forecastId) },
            _domainTypes,
            processThreshold);

        Assert.True(projected.IsTagStateUnsafe(forecastId));

        var serialized = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.Serialize(
            _domainTypes,
            serializeThreshold,
            projected);

        var deserialized = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.Deserialize(
            _domainTypes,
            serializeThreshold,
            serialized.Data);

        var deserializedStates = deserialized.GetCurrentTagStates();
        Assert.Single(deserializedStates);
        Assert.Contains(forecastId, deserializedStates.Keys);
        Assert.Equal(20, Assert.IsType<WeatherForecastState>(deserializedStates[forecastId].Payload).TemperatureC);
    }

    [Fact]
    public void GenericTagProjector_SerializationRoundTrip_PreservesSafeProgress_WhenSafeStateIsEmpty()
    {
        var forecastId = Guid.NewGuid();
        var createTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var deleteTime = createTime.AddMinutes(1);
        var processThreshold = SortableUniqueId.Generate(createTime.AddSeconds(-20), Guid.Empty);
        var serializeThreshold = SortableUniqueId.Generate(deleteTime.AddSeconds(20), Guid.Empty);

        var created = CreateEvent(
            new WeatherForecastCreated(forecastId, "Tokyo", DateOnly.FromDateTime(createTime), 20, "Sunny"),
            createTime,
            forecastId) with { Tags = new List<string>() };
        var deleted = CreateEvent(new WeatherForecastDeleted(forecastId), deleteTime, forecastId)
            with { Tags = new List<string>() };

        var createdProjection = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.Project(
            GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.GenerateInitialPayload(),
            created,
            new List<ITag> { new WeatherForecastTag(forecastId) },
            _domainTypes,
            processThreshold);
        var projected = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.Project(
            createdProjection,
            deleted,
            new List<ITag> { new WeatherForecastTag(forecastId) },
            _domainTypes,
            processThreshold);

        var safeProjectionBefore = projected.GetSafeProjection(new SortableUniqueId(serializeThreshold), _domainTypes);
        Assert.Empty(safeProjectionBefore.State.GetCurrentTagStates());
        Assert.Equal(2, safeProjectionBefore.Version);
        Assert.Equal(deleted.SortableUniqueIdValue, safeProjectionBefore.SafeLastSortableUniqueId);

        var serialized = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.Serialize(
            _domainTypes,
            serializeThreshold,
            projected);
        var deserialized = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.Deserialize(
            _domainTypes,
            serializeThreshold,
            serialized.Data);

        var safeProjectionAfter = deserialized.GetSafeProjection(new SortableUniqueId(serializeThreshold), _domainTypes);
        var unsafeProjectionAfter = deserialized.GetUnsafeProjection(_domainTypes);

        Assert.Empty(safeProjectionAfter.State.GetCurrentTagStates());
        Assert.Equal(2, safeProjectionAfter.Version);
        Assert.Equal(deleted.SortableUniqueIdValue, safeProjectionAfter.SafeLastSortableUniqueId);
        Assert.Equal(2, unsafeProjectionAfter.Version);
        Assert.Equal(deleted.SortableUniqueIdValue, unsafeProjectionAfter.LastSortableUniqueId);
    }

    [Fact]
    public void RestoredSnapshot_WrapperPreservesUnsafeState_ForGenericTagMultiProjector()
    {
        var forecastId = Guid.NewGuid();
        var eventTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var safeThreshold = SortableUniqueId.Generate(eventTime.AddSeconds(20), Guid.Empty);
        var weatherEvent = CreateEvent(
            new WeatherForecastCreated(
                forecastId,
                "Tokyo",
                DateOnly.FromDateTime(eventTime),
                20,
                "Sunny"),
            eventTime,
            forecastId) with { Tags = new List<string>() };

        var projected = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.Project(
            GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.GenerateInitialPayload(),
            weatherEvent,
            new List<ITag> { new WeatherForecastTag(forecastId) },
            _domainTypes,
            safeThreshold);

        var serialized = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.Serialize(
            _domainTypes,
            safeThreshold,
            projected);
        var deserialized = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.Deserialize(
            _domainTypes,
            safeThreshold,
            serialized.Data);

        var wrapper = Assert.IsType<DualStateProjectionWrapper<GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>>>(
            DualStateProjectionWrapperFactory.CreateFromRestoredSnapshot(
            deserialized,
            GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.MultiProjectorName,
            (ICoreMultiProjectorTypes)_domainTypes.MultiProjectorTypes,
            _domainTypes.JsonSerializerOptions,
            initialVersion: 1,
            initialLastEventId: weatherEvent.Id,
            initialLastSortableUniqueId: weatherEvent.SortableUniqueIdValue));

        var unsafeProjection = wrapper.GetUnsafeProjection(_domainTypes);
        var restoredStates = unsafeProjection.State.GetCurrentTagStates();

        Assert.Single(restoredStates);
        Assert.Contains(forecastId, restoredStates.Keys);
        Assert.Equal(20, Assert.IsType<WeatherForecastState>(restoredStates[forecastId].Payload).TemperatureC);
    }

    [Fact]
    public void RestoredSnapshot_WrapperPreservesAllItems_ForMultipleTagStates()
    {
        var eventTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var safeThreshold = SortableUniqueId.Generate(eventTime.AddSeconds(60), Guid.Empty);

        // Create 3 forecasts with different data
        var forecasts = new[]
        {
            (Id: Guid.NewGuid(), City: "Tokyo", Temp: 20, Time: eventTime),
            (Id: Guid.NewGuid(), City: "Osaka", Temp: 28, Time: eventTime.AddSeconds(1)),
            (Id: Guid.NewGuid(), City: "Sapporo", Temp: 5, Time: eventTime.AddSeconds(2)),
        };

        var projector = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.GenerateInitialPayload();

        foreach (var f in forecasts)
        {
            var ev = CreateEvent(
                new WeatherForecastCreated(f.Id, f.City, DateOnly.FromDateTime(f.Time), f.Temp, "Sunny"),
                f.Time,
                f.Id) with { Tags = new List<string>() };

            projector = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.Project(
                projector,
                ev,
                new List<ITag> { new WeatherForecastTag(f.Id) },
                _domainTypes,
                safeThreshold);
        }

        // Verify projector has 3 items before serialization
        Assert.Equal(3, projector.GetCurrentTagStates().Count);

        // Serialize → Deserialize (snapshot round-trip)
        var serialized = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.Serialize(
            _domainTypes,
            safeThreshold,
            projector);
        var deserialized = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.Deserialize(
            _domainTypes,
            safeThreshold,
            serialized.Data);

        // Wrap in DualStateProjectionWrapper as if restored from snapshot
        var lastForecast = forecasts[^1];
        var lastEvent = CreateEvent(
            new WeatherForecastCreated(lastForecast.Id, lastForecast.City, DateOnly.FromDateTime(lastForecast.Time), lastForecast.Temp, "Sunny"),
            lastForecast.Time,
            lastForecast.Id);

        var wrapper = Assert.IsType<DualStateProjectionWrapper<GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>>>(
            DualStateProjectionWrapperFactory.CreateFromRestoredSnapshot(
            deserialized,
            GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.MultiProjectorName,
            (ICoreMultiProjectorTypes)_domainTypes.MultiProjectorTypes,
            _domainTypes.JsonSerializerOptions,
            initialVersion: 3,
            initialLastEventId: lastEvent.Id,
            initialLastSortableUniqueId: lastEvent.SortableUniqueIdValue));

        // Verify ALL 3 items are preserved in the unsafe projection
        var unsafeProjection = wrapper.GetUnsafeProjection(_domainTypes);
        var restoredStates = unsafeProjection.State.GetCurrentTagStates();

        Assert.Equal(3, restoredStates.Count);

        foreach (var f in forecasts)
        {
            Assert.Contains(f.Id, restoredStates.Keys);
            var state = Assert.IsType<WeatherForecastState>(restoredStates[f.Id].Payload);
            Assert.Equal(f.City, state.Location);
            Assert.Equal(f.Temp, state.TemperatureC);
        }

        // Also verify GetStatePayloads (the method used by list queries) returns all 3
        var payloads = unsafeProjection.State.GetStatePayloads().ToList();
        Assert.Equal(3, payloads.Count);
    }

    private static Event CreateEvent(IEventPayload payload, DateTime timestamp, Guid forecastId)
    {
        var eventId = Guid.NewGuid();
        var sortableId = SortableUniqueId.Generate(timestamp, eventId);
        return new Event(
            payload,
            sortableId,
            payload.GetType().Name,
            eventId,
            new EventMetadata(eventId.ToString(), Guid.NewGuid().ToString(), "TestUser"),
            new List<string> { $"WeatherForecast:{forecastId}" });
    }
}
