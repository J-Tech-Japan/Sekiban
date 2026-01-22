using Microsoft.AspNetCore.Mvc;
using Sekiban.Dcb.Orleans.Grains;

namespace SekibanDcbDecider.ApiService.Endpoints;

public static class ProjectionEndpoints
{
    public static void MapProjectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/projections")
            .WithTags("Projections");

        group.MapPost("/persist", PersistProjectionStateAsync)
            .WithName("PersistProjectionState");

        group.MapPost("/deactivate", DeactivateProjectionAsync)
            .WithName("DeactivateProjection");

        group.MapPost("/refresh", RefreshProjectionAsync)
            .WithName("RefreshProjection");

        group.MapGet("/snapshot", GetProjectionSnapshotAsync)
            .WithName("GetProjectionSnapshot");

        group.MapPost("/overwrite-version", OverwriteProjectionPersistedVersionAsync)
            .WithName("OverwriteProjectionPersistedVersion");
    }

    private static async Task<IResult> PersistProjectionStateAsync(
        [FromQuery] string name,
        [FromServices] IClusterClient client,
        [FromServices] ILogger<Program> logger)
    {
        var start = DateTime.UtcNow;
        logger.LogDebug("PersistProjectionState request: name={Name}, start={Start:O}", name, start);
        var grain = client.GetGrain<IMultiProjectionGrain>(name);
        var rb = await grain.PersistStateAsync();
        var end = DateTime.UtcNow;
        var elapsedMs = (end - start).TotalMilliseconds;
        if (rb.IsSuccess)
        {
            logger.LogDebug("PersistProjectionState success: name={Name}, elapsed={ElapsedMs:F1}ms", name, elapsedMs);
            return Results.Ok(new { success = rb.GetValue(), elapsedMs });
        }
        var err = rb.GetException()?.Message;
        logger.LogWarning("PersistProjectionState failure: name={Name}, elapsed={ElapsedMs:F1}ms, error={Error}", name, elapsedMs, err);
        return Results.BadRequest(new { error = err, elapsedMs });
    }

    private static async Task<IResult> DeactivateProjectionAsync(
        [FromQuery] string name,
        [FromServices] IClusterClient client)
    {
        var grain = client.GetGrain<IMultiProjectionGrain>(name);
        await grain.RequestDeactivationAsync();
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> RefreshProjectionAsync(
        [FromQuery] string name,
        [FromServices] IClusterClient client)
    {
        var grain = client.GetGrain<IMultiProjectionGrain>(name);
        await grain.RefreshAsync();
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> GetProjectionSnapshotAsync(
        [FromQuery] string name,
        [FromQuery] bool? unsafeState,
        [FromServices] IClusterClient client)
    {
        var grain = client.GetGrain<IMultiProjectionGrain>(name);
        var rb = await grain.GetSnapshotJsonAsync(canGetUnsafeState: unsafeState ?? true);
        if (!rb.IsSuccess) return Results.BadRequest(new { error = rb.GetException()?.Message });
        return Results.Text(rb.GetValue(), "application/json");
    }

    private static async Task<IResult> OverwriteProjectionPersistedVersionAsync(
        [FromQuery] string name,
        [FromQuery] string newVersion,
        [FromServices] IClusterClient client)
    {
        var grain = client.GetGrain<IMultiProjectionGrain>(name);
        var ok = await grain.OverwritePersistedStateVersionAsync(newVersion);
        return ok ? Results.Ok(new { success = true }) : Results.BadRequest(new { error = "No persisted state to overwrite or invalid envelope" });
    }
}
