using Dapper;
using Dcb.EventSource.ClassRoom;
using Dcb.EventSource.MaterializedViews;
using Dcb.ImmutableModels.States.Student;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Sekiban.Dcb.MaterializedView.Orleans;

namespace SekibanDcbDecider.ApiService.Endpoints;

/// <summary>
///     Read-side endpoints backed by the PostgreSQL materialized view.
///     They mirror the in-memory projection endpoints in
///     <see cref="StudentEndpoints" /> and <see cref="ClassRoomEndpoints" />,
///     so the UI can switch between query modes without changing the rest of the request shape.
/// </summary>
public static class MaterializedViewEndpoints
{
    public static void MapMaterializedViewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var students = endpoints.MapGroup("/mv/students").WithTags("Materialized View");
        students.MapGet("/", GetStudentListAsync).WithName("GetStudentListMv");

        var classrooms = endpoints.MapGroup("/mv/classrooms").WithTags("Materialized View");
        classrooms.MapGet("/", GetClassRoomListAsync).WithName("GetClassRoomListMv");

        var enrollments = endpoints.MapGroup("/mv/enrollments").WithTags("Materialized View");
        enrollments.MapGet("/", GetEnrollmentListAsync).WithName("GetEnrollmentListMv");

        var status = endpoints.MapGroup("/mv").WithTags("Materialized View");
        status.MapGet("/status", GetStatusAsync).WithName("GetMaterializedViewStatus");
    }

    private static async Task<IResult> GetStudentListAsync(
        [FromQuery] string? waitForSortableUniqueId,
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromServices] IMvOrleansQueryAccessor mvQueryAccessor,
        [FromServices] ClassRoomEnrollmentMvV1 projector)
    {
        var context = await mvQueryAccessor.GetAsync(projector);
        if (!await TryWaitForReceivedAsync(context, waitForSortableUniqueId))
        {
            return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
        }

        var studentsTable = context.GetRequiredTable(ClassRoomEnrollmentMvV1.StudentsLogicalTable);
        var enrollmentsTable = context.GetRequiredTable(ClassRoomEnrollmentMvV1.EnrollmentsLogicalTable);

        await using var connection = new NpgsqlConnection(context.ConnectionString);
        await connection.OpenAsync();

        var (limit, offset) = ResolvePaging(pageNumber, pageSize);
        var students = (await connection.QueryAsync<StudentMvRow>(
            $"""
             SELECT student_id, name, max_class_count, enrolled_count,
                    _last_sortable_unique_id, _last_applied_at
             FROM {studentsTable.PhysicalTable}
             ORDER BY name
             LIMIT @Limit OFFSET @Offset;
             """,
            new { Limit = limit, Offset = offset })).ToList();

        var ids = students.Select(s => s.StudentId).ToArray();
        var enrollmentMap = ids.Length == 0
            ? new Dictionary<Guid, List<Guid>>()
            : (await connection.QueryAsync<(Guid student_id, Guid class_room_id)>(
                    $"""
                     SELECT student_id, class_room_id
                     FROM {enrollmentsTable.PhysicalTable}
                     WHERE student_id = ANY(@Ids);
                     """,
                    new { Ids = ids }))
                .GroupBy(row => row.student_id)
                .ToDictionary(g => g.Key, g => g.Select(r => r.class_room_id).ToList());

        var items = students
            .Select(s => new StudentState(
                s.StudentId,
                s.Name,
                s.MaxClassCount,
                enrollmentMap.TryGetValue(s.StudentId, out var ec) ? ec : []))
            .ToList();

        return Results.Ok(items);
    }

    private static async Task<IResult> GetClassRoomListAsync(
        [FromQuery] string? waitForSortableUniqueId,
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromServices] IMvOrleansQueryAccessor mvQueryAccessor,
        [FromServices] ClassRoomEnrollmentMvV1 projector)
    {
        var context = await mvQueryAccessor.GetAsync(projector);
        if (!await TryWaitForReceivedAsync(context, waitForSortableUniqueId))
        {
            return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
        }

        var classRoomsTable = context.GetRequiredTable(ClassRoomEnrollmentMvV1.ClassRoomsLogicalTable);

        await using var connection = new NpgsqlConnection(context.ConnectionString);
        await connection.OpenAsync();

        var (limit, offset) = ResolvePaging(pageNumber, pageSize);
        var rows = (await connection.QueryAsync<ClassRoomMvRow>(
            $"""
             SELECT class_room_id, name, max_students, enrolled_count,
                    _last_sortable_unique_id, _last_applied_at
             FROM {classRoomsTable.PhysicalTable}
             ORDER BY name
             LIMIT @Limit OFFSET @Offset;
             """,
            new { Limit = limit, Offset = offset })).ToList();

        var items = rows
            .Select(r => new ClassRoomItem
            {
                ClassRoomId = r.ClassRoomId,
                Name = r.Name,
                MaxStudents = r.MaxStudents,
                EnrolledCount = r.EnrolledCount,
                IsFull = r.EnrolledCount >= r.MaxStudents,
                RemainingCapacity = Math.Max(0, r.MaxStudents - r.EnrolledCount)
            })
            .ToList();

        return Results.Ok(items);
    }

    private static async Task<IResult> GetEnrollmentListAsync(
        [FromQuery] Guid? classRoomId,
        [FromQuery] Guid? studentId,
        [FromQuery] string? waitForSortableUniqueId,
        [FromServices] IMvOrleansQueryAccessor mvQueryAccessor,
        [FromServices] ClassRoomEnrollmentMvV1 projector)
    {
        var context = await mvQueryAccessor.GetAsync(projector);
        if (!await TryWaitForReceivedAsync(context, waitForSortableUniqueId))
        {
            return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
        }

        var enrollmentsTable = context.GetRequiredTable(ClassRoomEnrollmentMvV1.EnrollmentsLogicalTable);

        await using var connection = new NpgsqlConnection(context.ConnectionString);
        await connection.OpenAsync();

        var sql = $"SELECT student_id, class_room_id, enrolled_at, _last_sortable_unique_id FROM {enrollmentsTable.PhysicalTable}";
        var filters = new List<string>();
        var parameters = new DynamicParameters();
        if (classRoomId is { } cid)
        {
            filters.Add("class_room_id = @ClassRoomId");
            parameters.Add("ClassRoomId", cid);
        }
        if (studentId is { } sid)
        {
            filters.Add("student_id = @StudentId");
            parameters.Add("StudentId", sid);
        }
        if (filters.Count > 0)
        {
            sql += " WHERE " + string.Join(" AND ", filters);
        }
        sql += " ORDER BY enrolled_at DESC;";

        var rows = (await connection.QueryAsync<EnrollmentMvRow>(sql, parameters)).ToList();
        return Results.Ok(rows);
    }

    private static async Task<IResult> GetStatusAsync(
        [FromServices] IMvOrleansQueryAccessor mvQueryAccessor,
        [FromServices] ClassRoomEnrollmentMvV1 projector)
    {
        var context = await mvQueryAccessor.GetAsync(projector);
        var status = await context.Grain.GetStatusAsync();
        return Results.Ok(new
        {
            databaseType = context.DatabaseType,
            entries = context.Entries,
            status
        });
    }

    private static async Task<bool> TryWaitForReceivedAsync(
        MvOrleansQueryContext context,
        string? sortableUniqueId,
        int timeoutMs = 10_000)
    {
        if (string.IsNullOrWhiteSpace(sortableUniqueId))
        {
            return true;
        }

        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            if (await context.Grain.IsSortableUniqueIdReceived(sortableUniqueId))
            {
                return true;
            }
            await Task.Delay(100);
        }
        return false;
    }

    private static (int Limit, int Offset) ResolvePaging(int? pageNumber, int? pageSize)
    {
        var size = pageSize is > 0 ? pageSize.Value : 20;
        var page = pageNumber is > 0 ? pageNumber.Value : 1;
        return (size, (page - 1) * size);
    }
}
