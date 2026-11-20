using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Orleans.ServiceId;
using SekibanDcbDeciderAws.ApiService;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Dcb.Orleans.Grains;

namespace SekibanDcbDeciderAws.ApiService.Endpoints;

public static class ProjectionEndpoints
{
    public static void MapProjectionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/projections")
            .WithTags("Projections")
            .AddEndpointFilter<KnownProjectorFilter>();

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
        [FromServices] IServiceIdProvider serviceIdProvider,
        [FromServices] IClusterClient client,
        [FromServices] ILogger<Program> logger)
    {
        var start = DateTime.UtcNow;
        logger.LogDebug("PersistProjectionState request: name={Name}, start={Start:O}", name, start);
        var grain = client.GetGrain<IMultiProjectionGrain>(ServiceIdGrainKey.Build(serviceIdProvider.GetCurrentServiceId(), name));
        try
        {
            var saved = await grain.PersistStateAsync();
            var end = DateTime.UtcNow;
            var elapsedMs = (end - start).TotalMilliseconds;
            logger.LogDebug("PersistProjectionState success: name={Name}, elapsed={ElapsedMs:F1}ms", name, elapsedMs);
            return Results.Ok(new { success = saved, elapsedMs });
        }
        catch (Exception persistFailure)
        {
            var end = DateTime.UtcNow;
            var elapsedMs = (end - start).TotalMilliseconds;
            var err = persistFailure.Message;
            logger.LogWarning("PersistProjectionState failure: name={Name}, elapsed={ElapsedMs:F1}ms, error={Error}", name, elapsedMs, err);
            return ProjectionErrors.Map(persistFailure, Results.BadRequest(new { error = err, elapsedMs }));
        }
    }

    private static async Task<IResult> DeactivateProjectionAsync(
        [FromQuery] string name,
        [FromServices] IServiceIdProvider serviceIdProvider,
        [FromServices] IClusterClient client)
    {
        var grain = client.GetGrain<IMultiProjectionGrain>(ServiceIdGrainKey.Build(serviceIdProvider.GetCurrentServiceId(), name));
        await grain.RequestDeactivationAsync();
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> RefreshProjectionAsync(
        [FromQuery] string name,
        [FromServices] IServiceIdProvider serviceIdProvider,
        [FromServices] IClusterClient client)
    {
        var grain = client.GetGrain<IMultiProjectionGrain>(ServiceIdGrainKey.Build(serviceIdProvider.GetCurrentServiceId(), name));
        await grain.RefreshAsync();
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> GetProjectionSnapshotAsync(
        [FromQuery] string name,
        [FromQuery] bool? unsafeState,
        [FromServices] IServiceIdProvider serviceIdProvider,
        [FromServices] IClusterClient client)
    {
        var grain = client.GetGrain<IMultiProjectionGrain>(ServiceIdGrainKey.Build(serviceIdProvider.GetCurrentServiceId(), name));
        string snapshotJson;
        try
        {
            snapshotJson = await grain.GetSnapshotJsonAsync(canGetUnsafeState: unsafeState ?? true);
        }
        catch (Exception snapshotFailure)
        {
            return ProjectionErrors.Map(snapshotFailure, Results.BadRequest(new { error = snapshotFailure.Message }));
        }
        return Results.Text(snapshotJson, "application/json");
    }

    private static async Task<IResult> OverwriteProjectionPersistedVersionAsync(
        [FromQuery] string name,
        [FromQuery] string newVersion,
        [FromServices] IServiceIdProvider serviceIdProvider,
        [FromServices] IClusterClient client)
    {
        var grain = client.GetGrain<IMultiProjectionGrain>(ServiceIdGrainKey.Build(serviceIdProvider.GetCurrentServiceId(), name));
        var ok = await grain.OverwritePersistedStateVersionAsync(newVersion);
        return ok ? Results.Ok(new { success = true }) : Results.BadRequest(new { error = "No persisted state to overwrite or invalid envelope" });
    }
}
