using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Dcb.Domain.Projections;
using Dcb.Domain.Queries;
using Dcb.Domain.Weather;
using ResultBoxes;
using Sekiban.Dcb;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Xunit;

namespace Sekiban.Dcb.Tests;

public class WeatherForecastVersionQueryTests
{
    private readonly DcbDomainTypes _domainTypes;
    private readonly IEventStore _eventStore = new InMemoryEventStore();

    public WeatherForecastVersionQueryTests()
    {
        var eventTypes = new SimpleEventTypes();
        eventTypes.RegisterEventType<WeatherForecastCreated>(nameof(WeatherForecastCreated));
        eventTypes.RegisterEventType<WeatherForecastUpdated>(nameof(WeatherForecastUpdated));

        var tagTypes = new SimpleTagTypes();
        tagTypes.RegisterTagGroupType<WeatherForecastTag>();

        var tagProjectorTypes = new SimpleTagProjectorTypes();
        tagProjectorTypes.RegisterProjector<WeatherForecastProjector>();

        var tagStatePayloadTypes = new SimpleTagStatePayloadTypes();
        tagStatePayloadTypes.RegisterPayloadType<WeatherForecastState>();

        var multiProjectorTypes = new SimpleMultiProjectorTypes();
        multiProjectorTypes.RegisterProjector<WeatherForecastProjection>();

        var queryTypes = new SimpleQueryTypes();
        queryTypes.RegisterQuery<GetWeatherForecastCountQuery>();

        _domainTypes = new DcbDomainTypes(
            eventTypes,
            tagTypes,
            tagProjectorTypes,
            tagStatePayloadTypes,
            multiProjectorTypes,
            queryTypes,
            new JsonSerializerOptions());
    }

    private Event CreateEvent(IEventPayload payload, DateTime when, Guid id)
    {
        var sortableId = SortableUniqueId.Generate(when, Guid.NewGuid());
        var tags = new List<string> { $"WeatherForecast:{id}" };
        return new Event(
            payload,
            sortableId,
            payload.GetType().Name,
            Guid.NewGuid(),
            new EventMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "TestUser"),
            tags);
    }

    [Fact]
    public void CountQuery_Should_Return_Same_Safe_And_Unsafe_When_All_Events_Are_Safe()
    {
        var projector = WeatherForecastProjection.GenerateInitialPayload();
        var id = Guid.NewGuid();
    var safeThreshold = SortableUniqueId.Generate(DateTime.UtcNow.AddSeconds(-20), Guid.Empty);
        var ev = CreateEvent(new WeatherForecastCreated(id, "Loc", DateOnly.FromDateTime(DateTime.UtcNow.AddSeconds(-30)), 10, "Old"), DateTime.UtcNow.AddSeconds(-30), id);
        var tags = new List<ITag> { new WeatherForecastTag(id) };
        var result = WeatherForecastProjection.Project(projector, ev, tags, _domainTypes, safeThreshold).GetValue();
        var services = new ServiceCollection().AddSingleton(_domainTypes).BuildServiceProvider();
        var thresholdId = new SortableUniqueId(safeThreshold);
        var context = new QueryContext(
            serviceProvider: services,
            safeVersion: 1,
            safeWindowThreshold: safeThreshold,
            safeWindowThresholdTime: thresholdId.GetDateTime(),
            unsafeVersion: 1);
        var countResult = GetWeatherForecastCountQuery.HandleQuery(result, new GetWeatherForecastCountQuery(), context).GetValue();
        Assert.Equal(1, countResult.SafeVersion);
        Assert.Equal(1, countResult.UnsafeVersion);
    }

    [Fact]
    public void CountQuery_Should_Show_UnsafeVersion_Ahead_When_Recent_Event_Applied()
    {
        var projector = WeatherForecastProjection.GenerateInitialPayload();
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
    var safeThreshold = SortableUniqueId.Generate(now.AddSeconds(-20), Guid.Empty);

        // Safe event
        var ev1 = CreateEvent(new WeatherForecastCreated(id, "Loc", DateOnly.FromDateTime(now.AddSeconds(-40)), 10, "Old"), now.AddSeconds(-40), id);
        projector = WeatherForecastProjection.Project(projector, ev1, new List<ITag> { new WeatherForecastTag(id) }, _domainTypes, safeThreshold).GetValue();

        // Recent (unsafe) update
        var ev2 = CreateEvent(new WeatherForecastUpdated(id, "Loc", DateOnly.FromDateTime(now), 12, "New"), now.AddSeconds(-5), id);
        projector = WeatherForecastProjection.Project(projector, ev2, new List<ITag> { new WeatherForecastTag(id) }, _domainTypes, safeThreshold).GetValue();

        // In the simplified model we pass versions explicitly: safe version is 1 (only safe event), unsafe version 2 (after applying recent)
        var services = new ServiceCollection().AddSingleton(_domainTypes).BuildServiceProvider();
        var thresholdId = new SortableUniqueId(safeThreshold);
        var context = new QueryContext(
            serviceProvider: services,
            safeVersion: 1,
            safeWindowThreshold: safeThreshold,
            safeWindowThresholdTime: thresholdId.GetDateTime(),
            unsafeVersion: 2);
        var countResult = GetWeatherForecastCountQuery.HandleQuery(projector, new GetWeatherForecastCountQuery(), context).GetValue();
        Assert.Equal(1, countResult.SafeVersion);
        Assert.Equal(2, countResult.UnsafeVersion);
        Assert.True(countResult.UnsafeVersion > countResult.SafeVersion);
    }
}
