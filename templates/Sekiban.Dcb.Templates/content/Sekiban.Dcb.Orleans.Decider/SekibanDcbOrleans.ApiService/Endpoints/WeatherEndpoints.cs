using Dcb.Domain.Decider.Queries;
using Dcb.Domain.Decider.Weather;
using Dcb.ImmutableModels.Events.Weather;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Dcb;
using Sekiban.Dcb.Orleans.Grains;

namespace SekibanDcbOrleans.ApiService.Endpoints;

public static class WeatherEndpoints
{
    public static void MapWeatherEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/weatherforecast")
            .WithTags("Weather");

        // List endpoints
        group.MapGet("/", GetWeatherForecastListAsync)
            .WithName("GetWeatherForecast");

        group.MapGet("/generic", GetWeatherForecastListGenericAsync)
            .WithName("GetWeatherForecastGeneric");

        group.MapGet("/single", GetWeatherForecastListSingleAsync)
            .WithName("GetWeatherForecastSingle");

        // Count endpoints
        group.MapGet("/count", GetWeatherForecastCountAsync)
            .WithName("GetWeatherForecastCount");

        group.MapGet("/generic/count", GetWeatherForecastCountGenericAsync)
            .WithName("GetWeatherForecastCountGeneric");

        group.MapGet("/single/count", GetWeatherForecastCountSingleAsync)
            .WithName("GetWeatherForecastCountSingle");

        // Status endpoints
        group.MapGet("/status", GetWeatherForecastStatusAsync)
            .WithName("GetWeatherForecastStatus");

        group.MapGet("/generic/status", GetWeatherForecastGenericStatusAsync)
            .WithName("GetWeatherForecastGenericStatus");

        group.MapGet("/single/status", GetWeatherForecastSingleStatusAsync)
            .WithName("GetWeatherForecastSingleStatus");

        // Event statistics endpoints
        group.MapGet("/event-statistics", GetEventDeliveryStatisticsAsync)
            .WithName("GetEventDeliveryStatistics");

        group.MapGet("/generic/event-statistics", GetEventDeliveryStatisticsGenericAsync)
            .WithName("GetEventDeliveryStatisticsGeneric");

        group.MapGet("/single/event-statistics", GetEventDeliveryStatisticsSingleAsync)
            .WithName("GetEventDeliveryStatisticsSingle");

        // Command endpoints (separate group for clarity)
        var commandGroup = endpoints.MapGroup("")
            .WithTags("Weather");

        commandGroup.MapPost("/inputweatherforecast", CreateWeatherForecastAsync)
            .WithName("InputWeatherForecast");

        commandGroup.MapPost("/updateweatherforecastlocation", UpdateWeatherForecastLocationAsync)
            .WithName("UpdateWeatherForecastLocation");

        commandGroup.MapPost("/removeweatherforecast", RemoveWeatherForecastAsync)
            .WithName("RemoveWeatherForecast");
    }

    private static async Task<IResult> GetWeatherForecastListAsync(
        [FromQuery] string? waitForSortableUniqueId,
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromServices] ISekibanExecutor executor)
    {
        pageNumber ??= 1;
        pageSize ??= 100;
        var query = new GetWeatherForecastListQuery
        {
            WaitForSortableUniqueId = waitForSortableUniqueId,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
        var result = await executor.QueryAsync(query);
        return Results.Ok(result.Items);
    }

    private static async Task<IResult> GetWeatherForecastListGenericAsync(
        [FromQuery] string? waitForSortableUniqueId,
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromServices] ISekibanExecutor executor)
    {
        pageNumber ??= 1;
        pageSize ??= 100;
        var query = new GetWeatherForecastListGenericQuery
        {
            WaitForSortableUniqueId = waitForSortableUniqueId,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
        var result = await executor.QueryAsync(query);
        return Results.Ok(result.Items);
    }

    private static async Task<IResult> GetWeatherForecastListSingleAsync(
        [FromQuery] string? waitForSortableUniqueId,
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromServices] ISekibanExecutor executor)
    {
        pageNumber ??= 1;
        pageSize ??= 100;
        var query = new GetWeatherForecastListSingleQuery
        {
            WaitForSortableUniqueId = waitForSortableUniqueId,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
        var result = await executor.QueryAsync(query);
        return Results.Ok(result.Items);
    }

    private static async Task<IResult> GetWeatherForecastCountAsync(
        [FromQuery] string? waitForSortableUniqueId,
        [FromServices] ISekibanExecutor executor)
    {
        var query = new GetWeatherForecastCountQuery
        {
            WaitForSortableUniqueId = waitForSortableUniqueId
        };
        var countResult = await executor.QueryAsync(query);
        return Results.Ok(new
        {
            safeVersion = countResult.SafeVersion,
            unsafeVersion = countResult.UnsafeVersion,
            totalCount = countResult.TotalCount
        });
    }

    private static async Task<IResult> GetWeatherForecastCountGenericAsync(
        [FromQuery] string? waitForSortableUniqueId,
        [FromServices] ISekibanExecutor executor)
    {
        var query = new GetWeatherForecastCountGenericQuery
        {
            WaitForSortableUniqueId = waitForSortableUniqueId
        };
        var countResult = await executor.QueryAsync(query);
        return Results.Ok(new
        {
            safeVersion = countResult.SafeVersion,
            unsafeVersion = countResult.UnsafeVersion,
            totalCount = countResult.TotalCount,
            isGeneric = true
        });
    }

    private static async Task<IResult> GetWeatherForecastCountSingleAsync(
        [FromQuery] string? waitForSortableUniqueId,
        [FromServices] ISekibanExecutor executor)
    {
        var query = new GetWeatherForecastCountSingleQuery
        {
            WaitForSortableUniqueId = waitForSortableUniqueId
        };
        var countResult = await executor.QueryAsync(query);
        return Results.Ok(new
        {
            safeVersion = countResult.SafeVersion,
            unsafeVersion = countResult.UnsafeVersion,
            totalCount = countResult.TotalCount,
            isSingle = true
        });
    }

    private static async Task<IResult> GetWeatherForecastStatusAsync(
        [FromServices] IClusterClient client)
    {
        var grain = client.GetGrain<IMultiProjectionGrain>("WeatherForecastProjection");
        var status = await grain.GetStatusAsync();
        return Results.Ok(status);
    }

    private static async Task<IResult> GetWeatherForecastGenericStatusAsync(
        [FromServices] IClusterClient client)
    {
        var grain = client.GetGrain<IMultiProjectionGrain>("GenericTagMultiProjector_WeatherForecastProjector_WeatherForecast");
        var status = await grain.GetStatusAsync();
        return Results.Ok(status);
    }

    private static async Task<IResult> GetWeatherForecastSingleStatusAsync(
        [FromServices] IClusterClient client)
    {
        var grain = client.GetGrain<IMultiProjectionGrain>("WeatherForecastProjectorWithTagStateProjector");
        var status = await grain.GetStatusAsync();
        return Results.Ok(status);
    }

    private static async Task<IResult> GetEventDeliveryStatisticsAsync(
        [FromServices] IClusterClient client)
    {
        var grain = client.GetGrain<IMultiProjectionGrain>("WeatherForecastProjection");
        var stats = await grain.GetEventDeliveryStatisticsAsync();
        return Results.Ok(stats);
    }

    private static async Task<IResult> GetEventDeliveryStatisticsGenericAsync(
        [FromServices] IClusterClient client)
    {
        var grain = client.GetGrain<IMultiProjectionGrain>("GenericTagMultiProjector_WeatherForecastProjector_WeatherForecast");
        var stats = await grain.GetEventDeliveryStatisticsAsync();
        return Results.Ok(stats);
    }

    private static async Task<IResult> GetEventDeliveryStatisticsSingleAsync(
        [FromServices] IClusterClient client)
    {
        var grain = client.GetGrain<IMultiProjectionGrain>("WeatherForecastProjectorWithTagStateProjector");
        var stats = await grain.GetEventDeliveryStatisticsAsync();
        return Results.Ok(stats);
    }

    private static async Task<IResult> CreateWeatherForecastAsync(
        [FromBody] CreateWeatherForecast command,
        [FromServices] ISekibanExecutor executor)
    {
        var result = await executor.ExecuteAsync(command);
        var createdEvent = result.Events.FirstOrDefault(m => m.Payload is WeatherForecastCreated)?.Payload.As<WeatherForecastCreated>();
        return Results.Ok(new
        {
            success = true,
            eventId = result.EventId,
            aggregateId = createdEvent?.ForecastId ?? command.ForecastId,
            sortableUniqueId = result.SortableUniqueId
        });
    }

    private static async Task<IResult> UpdateWeatherForecastLocationAsync(
        [FromBody] ChangeLocationName command,
        [FromServices] ISekibanExecutor executor)
    {
        var result = await executor.ExecuteAsync(command);
        return Results.Ok(new
        {
            success = true,
            eventId = result.EventId,
            aggregateId = command.ForecastId,
            sortableUniqueId = result.SortableUniqueId
        });
    }

    private static async Task<IResult> RemoveWeatherForecastAsync(
        [FromBody] DeleteWeatherForecast command,
        [FromServices] ISekibanExecutor executor)
    {
        var result = await executor.ExecuteAsync(command);
        return Results.Ok(new
        {
            success = true,
            eventId = result.EventId,
            aggregateId = command.ForecastId,
            sortableUniqueId = result.SortableUniqueId
        });
    }
}
