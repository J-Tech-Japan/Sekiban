using Dcb.EventSource.MeetingRoom.Queries;
using Dcb.EventSource.MeetingRoom.Room;
using Dcb.MeetingRoomModels.Events.Room;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Dcb;

namespace SekibanDcbDeciderAws.ApiService.Endpoints;

public static class RoomEndpoints
{
    public static void MapRoomEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/rooms")
            .WithTags("MeetingRoom - Rooms")
            .RequireAuthorization();

        // Query endpoints
        group.MapGet("/", GetRoomListAsync)
            .WithName("GetRooms");

        // Command endpoints
        group.MapPost("/", CreateRoomAsync)
            .WithName("CreateRoom");

        group.MapPut("/{roomId:guid}", UpdateRoomAsync)
            .WithName("UpdateRoom");
    }

    private static async Task<IResult> GetRoomListAsync(
        [FromQuery] string? waitForSortableUniqueId,
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromServices] ISekibanExecutor executor)
    {
        pageNumber ??= 1;
        pageSize ??= 100;
        var query = new GetRoomListQuery
        {
            WaitForSortableUniqueId = waitForSortableUniqueId,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
        var result = await executor.QueryAsync(query);
        return Results.Ok(result.Items);
    }

    private static async Task<IResult> CreateRoomAsync(
        [FromBody] CreateRoom command,
        [FromServices] ISekibanExecutor executor)
    {
        var result = await executor.ExecuteAsync(command);
        var createdEvent = result.Events.FirstOrDefault(m => m.Payload is RoomCreated)?.Payload.As<RoomCreated>();
        return Results.Ok(new
        {
            success = true,
            eventId = result.EventId,
            roomId = createdEvent?.RoomId ?? command.RoomId,
            sortableUniqueId = result.SortableUniqueId
        });
    }

    private static async Task<IResult> UpdateRoomAsync(
        Guid roomId,
        [FromBody] UpdateRoom command,
        [FromServices] ISekibanExecutor executor)
    {
        var updatedCommand = command with { RoomId = roomId };
        var result = await executor.ExecuteAsync(updatedCommand);
        return Results.Ok(new
        {
            success = true,
            eventId = result.EventId,
            roomId = roomId,
            sortableUniqueId = result.SortableUniqueId
        });
    }
}
