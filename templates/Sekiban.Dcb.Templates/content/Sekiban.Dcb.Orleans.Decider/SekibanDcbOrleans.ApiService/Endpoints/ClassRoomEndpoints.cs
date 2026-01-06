using Dcb.Domain.Decider.ClassRoom;
using Dcb.Domain.Decider.Queries;
using Dcb.ImmutableModels.Tags;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Dcb;
using Sekiban.Dcb.Tags;

namespace SekibanDcbOrleans.ApiService.Endpoints;

public static class ClassRoomEndpoints
{
    public static void MapClassRoomEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/classrooms")
            .WithTags("ClassRooms");

        group.MapPost("/", CreateClassRoomAsync)
            .WithName("CreateClassRoom");

        group.MapGet("/", GetClassRoomListAsync)
            .WithName("GetClassRoomList");

        group.MapGet("/{classRoomId:guid}", GetClassRoomAsync)
            .WithName("GetClassRoom");
    }

    private static async Task<IResult> CreateClassRoomAsync(
        [FromBody] CreateClassRoom command,
        [FromServices] ISekibanExecutor executor)
    {
        var result = await executor.ExecuteAsync(command, CreateClassRoomHandler.HandleAsync);
        return Results.Ok(new
        {
            classRoomId = command.ClassRoomId,
            eventId = result.EventId,
            sortableUniqueId = result.SortableUniqueId,
            message = "ClassRoom created successfully"
        });
    }

    private static async Task<IResult> GetClassRoomListAsync(
        [FromServices] ISekibanExecutor executor,
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromQuery] string? waitForSortableUniqueId)
    {
        var query = new GetClassRoomListQuery
        {
            PageNumber = pageNumber ?? 1,
            PageSize = pageSize ?? 20,
            WaitForSortableUniqueId = waitForSortableUniqueId
        };
        var result = await executor.QueryAsync(query);
        return Results.Ok(result.Items);
    }

    private static async Task<IResult> GetClassRoomAsync(
        Guid classRoomId,
        [FromServices] ISekibanExecutor executor)
    {
        var tag = new ClassRoomTag(classRoomId);
        var state = await executor.GetTagStateAsync(new TagStateId(tag, nameof(ClassRoomProjector)));

        return Results.Ok(new
        {
            classRoomId,
            payload = state.Payload,
            version = state.Version
        });
    }
}
