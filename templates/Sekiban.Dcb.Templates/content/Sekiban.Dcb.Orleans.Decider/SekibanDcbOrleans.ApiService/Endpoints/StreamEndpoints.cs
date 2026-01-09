using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SekibanDcbOrleans.ApiService.Realtime;

namespace SekibanDcbOrleans.ApiService.Endpoints;

public static class StreamEndpoints
{
    public static void MapStreamEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/stream")
            .WithTags("Realtime Streams");

        group.MapGet("/reservations", StreamReservationsAsync)
            .RequireAuthorization();

        group.MapGet("/approvals", StreamApprovalsAsync)
            .RequireAuthorization("AdminOnly");
    }

    private static Task StreamReservationsAsync(
        HttpContext context,
        [FromServices] SseTopicHub hub,
        [FromServices] IOptions<JsonOptions> jsonOptions) =>
        StreamTopicAsync(context, hub, StreamTopics.Reservations, jsonOptions.Value.JsonSerializerOptions);

    private static Task StreamApprovalsAsync(
        HttpContext context,
        [FromServices] SseTopicHub hub,
        [FromServices] IOptions<JsonOptions> jsonOptions) =>
        StreamTopicAsync(context, hub, StreamTopics.Approvals, jsonOptions.Value.JsonSerializerOptions);

    private static async Task StreamTopicAsync(
        HttpContext context,
        SseTopicHub hub,
        string topic,
        JsonSerializerOptions jsonOptions)
    {
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.Headers.ContentType = "text/event-stream";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        var cancellationToken = context.RequestAborted;
        var subscription = hub.Subscribe(topic, cancellationToken);
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        var writeLock = new SemaphoreSlim(1, 1);

        async Task WriteAsync(string payload)
        {
            await writeLock.WaitAsync(cancellationToken);
            try
            {
                await context.Response.WriteAsync(payload, cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }
            finally
            {
                writeLock.Release();
            }
        }

        var pingTask = Task.Run(async () =>
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await WriteAsync(": ping\n\n");
            }
        }, cancellationToken);

        try
        {
            await foreach (var update in subscription.Reader.ReadAllAsync(cancellationToken))
            {
                var json = JsonSerializer.Serialize(update, jsonOptions);
                await WriteAsync($"data: {json}\n\n");
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected.
        }
        finally
        {
            subscription.Dispose();
            timer.Dispose();
            writeLock.Dispose();
            try
            {
                await pingTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when client disconnects.
            }
        }
    }
}
