using Dcb.EventSource.Queries;
using Dcb.EventSource.Student;
using Dcb.ImmutableModels.Tags;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Dcb;
using Sekiban.Dcb.Tags;

namespace SekibanDcbDecider.ApiService.Endpoints;

public static class StudentEndpoints
{
    public static void MapStudentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/students")
            .WithTags("Students");

        group.MapPost("/", CreateStudentAsync)
            .WithName("CreateStudent");

        group.MapGet("/", GetStudentListAsync)
            .WithName("GetStudentList");

        group.MapGet("/{studentId:guid}", GetStudentAsync)
            .WithName("GetStudent");
    }

    private static async Task<IResult> CreateStudentAsync(
        [FromBody] CreateStudent command,
        [FromServices] ISekibanExecutor executor)
    {
        var execution = await executor.ExecuteAsync(command);
        return Results.Ok(new
        {
            studentId = command.StudentId,
            eventId = execution.EventId,
            sortableUniqueId = execution.SortableUniqueId,
            message = "Student created successfully"
        });
    }

    private static async Task<IResult> GetStudentListAsync(
        [FromServices] ISekibanExecutor executor,
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromQuery] string? waitForSortableUniqueId)
    {
        var query = new GetStudentListQuery
        {
            PageNumber = pageNumber ?? 1,
            PageSize = pageSize ?? 20,
            WaitForSortableUniqueId = waitForSortableUniqueId
        };
        var result = await executor.QueryAsync(query);
        return Results.Ok(result.Items);
    }

    private static async Task<IResult> GetStudentAsync(
        Guid studentId,
        [FromServices] ISekibanExecutor executor)
    {
        var tag = new StudentTag(studentId);
        var state = await executor.GetTagStateAsync(new TagStateId(tag, nameof(StudentProjector)));

        return Results.Ok(new
        {
            studentId,
            payload = state.Payload as dynamic,
            version = state.Version
        });
    }
}
