using Dcb.EventSource.Queries;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Dcb;
using Sekiban.Dcb.Storage;

namespace SekibanDcbOrleans.ApiService.Endpoints;

public static class DebugEndpoints
{
    public static void MapDebugEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/debug")
            .WithTags("Debug");

        group.MapGet("/events", GetEventsAsync)
            .WithName("DebugGetEvents");

        group.MapGet("/health", GetHealthAsync)
            .WithName("HealthCheck");

        group.MapGet("/orleans/test", TestOrleansAsync)
            .WithName("TestOrleans");
    }

    private static async Task<IResult> GetEventsAsync(
        [FromServices] IEventStore eventStore)
    {
        var result = await eventStore.ReadAllEventsAsync();
        var events = result.GetValue().ToList();
        Console.WriteLine($"[Debug] ReadAllEventsAsync returned {events.Count} events");
        return Results.Ok(new
        {
            totalEvents = events.Count,
            events = events.Select(e => new
            {
                id = e.Id,
                type = e.EventType,
                sortableId = e.SortableUniqueIdValue,
                tags = e.Tags
            })
        });
    }

    private static IResult GetHealthAsync()
    {
        return Results.Ok("Healthy");
    }

    private static async Task<IResult> TestOrleansAsync(
        [FromServices] ISekibanExecutor executor,
        [FromServices] ILogger<Program> logger)
    {
        logger.LogInformation("Testing Orleans connectivity...");

        var query = new GetWeatherForecastListQuery();
        var result = await executor.QueryAsync(query);

        return Results.Ok(new
        {
            status = "Orleans is working",
            message = "Successfully executed query through Orleans",
            itemCount = result.TotalCount
        });
    }
}
