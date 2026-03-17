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
            forecastId);

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
