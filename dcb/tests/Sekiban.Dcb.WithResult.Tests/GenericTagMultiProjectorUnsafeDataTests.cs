using Dcb.Domain.Weather;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using Dcb.Domain.Projections;

namespace Sekiban.Dcb.Tests;

/// <summary>
/// Test to verify that GenericTagMultiProjector properly handles unsafe data
/// </summary>
public class GenericTagMultiProjectorUnsafeDataTests
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly DateTime _baseTime = DateTime.UtcNow;
    private readonly TimeProvider _timeProvider;

    public GenericTagMultiProjectorUnsafeDataTests()
    {
        _timeProvider = TimeProvider.System;
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<WeatherForecastCreated>("WeatherForecastCreated");
        eventTypes.RegisterEventType<WeatherForecastUpdated>("WeatherForecastUpdated");
        eventTypes.RegisterEventType<WeatherForecastDeleted>("WeatherForecastDeleted");

        var tagTypes = new SimpleTagTypes();
        tagTypes.RegisterTagGroupType<WeatherForecastTag>();

        var tagProjectorTypes = new SimpleTagProjectorTypes();
        tagProjectorTypes.RegisterProjector<WeatherForecastProjector>();

        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        tagStatePayloadTypes.RegisterPayloadType<WeatherForecastState>();

        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjector<GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>>();
        multiProjectorTypes.RegisterProjector<WeatherForecastProjection>();

        _domainTypes = new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            tagStatePayloadTypes,
            multiProjectorTypes,
            new SimpleQueryTypes());
    }

    [Fact]
    public void GenericTagMultiProjector_Should_Handle_Unsafe_Data_Like_Manual_Implementation()
    {
        // Arrange
        var forecastId1 = Guid.NewGuid();
        var forecastId2 = Guid.NewGuid();

        // Create events - one old (safe) and one recent (unsafe)
        // Use the actual domain events from Dcb.Domain.Weather namespace
        var oldEvent = CreateEvent(
            new WeatherForecastCreated(forecastId1, "Tokyo", DateOnly.FromDateTime(_baseTime), 20, "Sunny"),
            _baseTime.AddSeconds(-30), // 30 seconds ago - safe
            forecastId1);

        Console.WriteLine($"[Test] Created old event - Type: {oldEvent.EventType}, Payload type: {oldEvent.Payload?.GetType().Name}, Tags: {string.Join(", ", oldEvent.Tags)}");

        var recentEvent = CreateEvent(
            new WeatherForecastCreated(forecastId2, "Osaka", DateOnly.FromDateTime(_baseTime), 25, "Cloudy"),
            _baseTime.AddSeconds(-5), // 5 seconds ago - unsafe
            forecastId2);

        Console.WriteLine($"[Test] Created recent event - Type: {recentEvent.EventType}, Payload type: {recentEvent.Payload?.GetType().Name}, Tags: {string.Join(", ", recentEvent.Tags)}");

        // Act - Test GenericTagMultiProjector
        var genericProjector = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.GenerateInitialPayload();

        // Process old event
        var safeThreshold = SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(-20), Guid.Empty);
        var result1 = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.Project(
            genericProjector,
            oldEvent,
            new List<ITag> { new WeatherForecastTag(forecastId1) },
            _domainTypes,
            safeThreshold);
        genericProjector = result1.GetValue();

        // Process recent event
        var result2 = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.Project(
            genericProjector,
            recentEvent,
            new List<ITag> { new WeatherForecastTag(forecastId2) },
            _domainTypes,
            safeThreshold);
        genericProjector = result2.GetValue();

        // Act - Test Manual Implementation (WeatherForecastProjection)
        var manualProjector = WeatherForecastProjection.GenerateInitialPayload();

        Console.WriteLine($"[Test] Initial manual projector state count: {manualProjector.GetCurrentForecasts().Count}");

        // Process old event
        Console.WriteLine($"[Test] Processing old event with tags: {string.Join(", ", new List<ITag> { new WeatherForecastTag(forecastId1) }.Select(t => t.ToString()))}");
        var manualResult1 = WeatherForecastProjection.Project(
            manualProjector,
            oldEvent,
            new List<ITag> { new WeatherForecastTag(forecastId1) },
            _domainTypes,
            safeThreshold);
        manualProjector = manualResult1.GetValue();
        Console.WriteLine($"[Test] After old event, manual projector state count: {manualProjector.GetCurrentForecasts().Count}");

        // Process recent event
        Console.WriteLine($"[Test] Processing recent event with tags: {string.Join(", ", new List<ITag> { new WeatherForecastTag(forecastId2) }.Select(t => t.ToString()))}");
        var manualResult2 = WeatherForecastProjection.Project(
            manualProjector,
            recentEvent,
            new List<ITag> { new WeatherForecastTag(forecastId2) },
            _domainTypes,
            safeThreshold);
        manualProjector = manualResult2.GetValue();
        Console.WriteLine($"[Test] After recent event, manual projector state count: {manualProjector.GetCurrentForecasts().Count}");

        // Assert - Both should handle unsafe data the same way

        // Check manual implementation (WeatherForecastProjection)
        var manualCurrentForecasts = manualProjector.GetCurrentForecasts();

        // Prepare parameters for GetSafeForecasts
        var threshold = SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(-20), Guid.Empty);
        Func<Event, IEnumerable<Guid>> getAffectedIds = evt =>
        {
            if (evt.Payload is WeatherForecastCreated created) return new[] { created.ForecastId };
            if (evt.Payload is WeatherForecastUpdated updated) return new[] { updated.ForecastId };
            if (evt.Payload is WeatherForecastDeleted deleted) return new[] { deleted.ForecastId };
            if (evt.Payload is LocationNameChanged changed) return new[] { changed.ForecastId };
            return Enumerable.Empty<Guid>();
        };
        Func<Guid, WeatherForecastItem?, Event, WeatherForecastItem?> projectItem = (id, current, evt) =>
            evt.Payload switch
            {
                WeatherForecastCreated created => new WeatherForecastItem(
                    created.ForecastId, created.Location, created.Date.ToDateTime(TimeOnly.MinValue), created.TemperatureC, created.Summary, new SortableUniqueId(evt.SortableUniqueIdValue).GetDateTime()),
                WeatherForecastUpdated updated => current != null
                    ? current with { Date = updated.Date.ToDateTime(TimeOnly.MinValue), TemperatureC = updated.TemperatureC, Summary = updated.Summary, LastUpdated = new SortableUniqueId(evt.SortableUniqueIdValue).GetDateTime() }
                    : null,
                LocationNameChanged changed => current != null
                    ? current with { Location = changed.NewLocationName, LastUpdated = new SortableUniqueId(evt.SortableUniqueIdValue).GetDateTime() }
                    : null,
                WeatherForecastDeleted => null,
                _ => current
            };

        // SafeUnsafeProjectionState now manages safe/unsafe internally
        // We can only check current state and whether items are unsafe

        Assert.Equal(2, manualCurrentForecasts.Count);

        // Check generic implementation (GenericTagMultiProjector)
        var genericCurrentStates = genericProjector.GetCurrentTagStates();

        // For generic projector, need to provide projection functions for TagState
        Func<Event, IEnumerable<Guid>> getTagAffectedIds = evt =>
        {
            if (evt.Payload is WeatherForecastCreated created) return new[] { created.ForecastId };
            if (evt.Payload is WeatherForecastUpdated updated) return new[] { updated.ForecastId };
            if (evt.Payload is WeatherForecastDeleted deleted) return new[] { deleted.ForecastId };
            if (evt.Payload is LocationNameChanged changed) return new[] { changed.ForecastId };
            return Enumerable.Empty<Guid>();
        };

        Func<Guid, TagState?, Event, TagState?> projectTagState = (id, current, evt) =>
        {
            // This is a simplified projection for testing
            if (current == null)
            {
                var tagStateId = new TagStateId(new WeatherForecastTag(id), "WeatherForecastProjector");
                current = TagState.GetEmpty(tagStateId);
            }
            var newPayload = WeatherForecastProjector.Project(current.Payload, evt);
            return current with
            {
                Payload = newPayload,
                Version = current.Version + 1,
                LastSortedUniqueId = evt.SortableUniqueIdValue
            };
        };

        var genericSafeStates = genericProjector.GetSafeTagStates(threshold, getTagAffectedIds, projectTagState);

        // This is where the test might fail if GenericTagMultiProjector doesn't handle unsafe data correctly
        Assert.Equal(2, genericCurrentStates.Count);
        Assert.Single(genericSafeStates);

        // Verify the actual state content
        Assert.Contains(forecastId1, genericCurrentStates.Keys);
        Assert.Contains(forecastId2, genericCurrentStates.Keys);
        Assert.Contains(forecastId1, genericSafeStates.Keys);
        Assert.DoesNotContain(forecastId2, genericSafeStates.Keys); // Recent should not be in safe

        // Verify state payloads
        var state1 = genericCurrentStates[forecastId1].Payload as WeatherForecastState;
        var state2 = genericCurrentStates[forecastId2].Payload as WeatherForecastState;

        Assert.NotNull(state1);
        Assert.NotNull(state2);
        Assert.Equal(20, state1.TemperatureC);
        Assert.Equal(25, state2.TemperatureC);
    }

    [Fact]
    public void GenericTagMultiProjector_Should_Handle_Safe_And_Unsafe_Events_With_Fixed_Time()
    {
        // Arrange
        var forecastId = Guid.NewGuid();
        var testTime = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var testTimeProvider = new TestTimeProvider(testTime);
        var genericProjector = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.GenerateInitialPayload();

        // Create event that's safe (25 seconds ago from test time)
        var safeEvent = CreateEvent(
            new WeatherForecastCreated(forecastId, "Tokyo", DateOnly.FromDateTime(testTime), 20, "Sunny"),
            testTime.AddSeconds(-25), // 25 seconds ago - safe (window is 20 seconds)
            forecastId);

        // Process the safe event
        var safeThreshold2 = SortableUniqueId.Generate(testTime.AddSeconds(-20), Guid.Empty);
        var result = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.Project(
            genericProjector,
            safeEvent,
            new List<ITag> { new WeatherForecastTag(forecastId) },
            _domainTypes,
            safeThreshold2);
        genericProjector = result.GetValue();

        // Assert - initial state recorded
        var currentStates = genericProjector.GetCurrentTagStates();

        // Prepare projection functions for safe state
        var threshold2 = SortableUniqueId.Generate(testTime.AddSeconds(-20), Guid.Empty);
        Func<Event, IEnumerable<Guid>> getIds2 = evt =>
        {
            if (evt.Payload is WeatherForecastCreated created) return new[] { created.ForecastId };
            if (evt.Payload is WeatherForecastUpdated updated) return new[] { updated.ForecastId };
            if (evt.Payload is WeatherForecastDeleted deleted) return new[] { deleted.ForecastId };
            if (evt.Payload is LocationNameChanged changed) return new[] { changed.ForecastId };
            return Enumerable.Empty<Guid>();
        };
        Func<Guid, TagState?, Event, TagState?> projectTagState2 = (id, current, evt) =>
        {
            if (current == null)
            {
                var tagStateId = new TagStateId(new WeatherForecastTag(id), "WeatherForecastProjector");
                current = TagState.GetEmpty(tagStateId);
            }
            var newPayload = WeatherForecastProjector.Project(current.Payload, evt);
            return current with
            {
                Payload = newPayload,
                Version = current.Version + 1,
                LastSortedUniqueId = evt.SortableUniqueIdValue
            };
        };

        var safeStates = genericProjector.GetSafeTagStates(threshold2, getIds2, projectTagState2);
        Assert.Single(currentStates);
        Assert.Single(safeStates); // Should be in safe

        // Now create an event that's recent (unsafe - 19 seconds ago)
        var unsafeEvent = CreateEvent(
            new WeatherForecastUpdated(forecastId, "Tokyo", DateOnly.FromDateTime(testTime), 22, "Partly Cloudy"),
            testTime.AddSeconds(-19), // 19 seconds ago - unsafe
            forecastId);

        // Process the unsafe event with same time provider
        result = GenericTagMultiProjector<WeatherForecastProjector, WeatherForecastTag>.Project(
            genericProjector,
            unsafeEvent,
            new List<ITag> { new WeatherForecastTag(forecastId) },
            _domainTypes,
            safeThreshold2);
        genericProjector = result.GetValue();

        // Assert - updated state reflects latest event
        currentStates = genericProjector.GetCurrentTagStates();
        safeStates = genericProjector.GetSafeTagStates(threshold2, getIds2, projectTagState2);
        Assert.Single(currentStates);
        Assert.Single(safeStates); // Safe state should still exist

        // Verify current state has the update
        var currentState = currentStates[forecastId].Payload as WeatherForecastState;
        Assert.NotNull(currentState);
        Assert.Equal(22, currentState.TemperatureC);

        // Verify safe state has the original
        var safeState = safeStates[forecastId].Payload as WeatherForecastState;
        Assert.NotNull(safeState);
        Assert.Equal(20, safeState.TemperatureC);
    }

    private Event CreateEvent(IEventPayload payload, DateTime timestamp, Guid forecastId)
    {
        var eventId = Guid.NewGuid();
        var sortableId = SortableUniqueId.Generate(timestamp, eventId);
        var metadata = new EventMetadata(eventId.ToString(), Guid.NewGuid().ToString(), "TestUser");

        // Add tag to event - using the correct format
        var tags = new List<string> { $"WeatherForecast:{forecastId}" };

        return new Event(
            payload,
            sortableId,
            payload.GetType().Name,
            eventId,
            metadata,
            tags);
    }

    // Remove duplicate definitions and use the actual domain events from Dcb.Domain.Weather namespace

    /// <summary>
    /// Test time provider for controlling time in tests
    /// </summary>
    private class TestTimeProvider : TimeProvider
    {
        private readonly DateTime _currentTime;

        public TestTimeProvider(DateTime currentTime)
        {
            _currentTime = currentTime;
        }

        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(_currentTime, TimeSpan.Zero);
    }
}
