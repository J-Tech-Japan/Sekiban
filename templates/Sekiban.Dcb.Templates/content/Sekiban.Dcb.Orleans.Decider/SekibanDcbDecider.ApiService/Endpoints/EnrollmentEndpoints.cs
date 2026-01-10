using Dcb.EventSource.Enrollment;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Dcb;

namespace SekibanDcbDecider.ApiService.Endpoints;

public static class EnrollmentEndpoints
{
    public static void MapEnrollmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/enrollments")
            .WithTags("Enrollments");

        group.MapPost("/add", EnrollStudentAsync)
            .WithName("EnrollStudent");

        group.MapPost("/drop", DropStudentAsync)
            .WithName("DropStudent");
    }

    private static async Task<IResult> EnrollStudentAsync(
        [FromBody] EnrollStudentInClassRoom command,
        [FromServices] ISekibanExecutor executor)
    {
        var result = await executor.ExecuteAsync(command, EnrollStudentInClassRoomHandler.HandleAsync);
        return Results.Ok(new
        {
            studentId = command.StudentId,
            classRoomId = command.ClassRoomId,
            eventId = result.EventId,
            sortableUniqueId = result.SortableUniqueId,
            message = "Student enrolled successfully"
        });
    }

    private static async Task<IResult> DropStudentAsync(
        [FromBody] DropStudentFromClassRoom command,
        [FromServices] ISekibanExecutor executor)
    {
        var result = await executor.ExecuteAsync(command, DropStudentFromClassRoomHandler.HandleAsync);
        return Results.Ok(new
        {
            studentId = command.StudentId,
            classRoomId = command.ClassRoomId,
            eventId = result.EventId,
            sortableUniqueId = result.SortableUniqueId,
            message = "Student dropped successfully"
        });
    }
}
