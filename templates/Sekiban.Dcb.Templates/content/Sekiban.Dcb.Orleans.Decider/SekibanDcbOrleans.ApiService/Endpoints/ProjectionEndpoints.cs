using Microsoft.AspNetCore.Mvc;
using Sekiban.Dcb.Orleans.Grains;

namespace SekibanDcbOrleans.ApiService.Endpoints;

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
        [FromServices] IClusterClient client)
    {
        var start = DateTime.UtcNow;
        Console.WriteLine($"[PersistEndpoint] Request name={name} start={start:O}");
        var grain = client.GetGrain<IMultiProjectionGrain>(name);
        var rb = await grain.PersistStateAsync();
        var end = DateTime.UtcNow;
        if (rb.IsSuccess)
        {
            Console.WriteLine($"[PersistEndpoint] Success name={name} elapsed={(end - start).TotalMilliseconds:F1}ms");
            return Results.Ok(new { success = rb.GetValue(), elapsedMs = (end - start).TotalMilliseconds });
        }
        var err = rb.GetException()?.Message;
        Console.WriteLine($"[PersistEndpoint] Failure name={name} elapsed={(end - start).TotalMilliseconds:F1}ms error={err}");
        return Results.BadRequest(new { error = err, elapsedMs = (end - start).TotalMilliseconds });
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
