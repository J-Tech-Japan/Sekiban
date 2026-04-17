using Dcb.ImmutableModels.Events.ClassRoom;
using Dcb.ImmutableModels.Events.Enrollment;
using Dcb.ImmutableModels.Events.Student;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MaterializedView;

namespace Dcb.EventSource.MaterializedViews;

/// <summary>
///     Materialized view for classrooms, students and enrollments.
///     Mirrors what the in-memory <c>ClassRoomListProjection</c> and
///     <c>StudentListProjection</c> expose, but persisted into PostgreSQL tables
///     so that they can be queried as a SQL read model.
/// </summary>
public sealed class ClassRoomEnrollmentMvV1 : IMaterializedViewProjector
{
    public const string ClassRoomsLogicalTable = "classrooms";
    public const string StudentsLogicalTable = "students";
    public const string EnrollmentsLogicalTable = "enrollments";

    public string ViewName => "ClassRoomEnrollment";
    public int ViewVersion => 1;

    public MvTable ClassRooms { get; private set; } = default!;
    public MvTable Students { get; private set; } = default!;
    public MvTable Enrollments { get; private set; } = default!;

    public async Task InitializeAsync(IMvInitContext ctx, CancellationToken cancellationToken = default)
    {
        ClassRooms = ctx.RegisterTable(ClassRoomsLogicalTable);
        Students = ctx.RegisterTable(StudentsLogicalTable);
        Enrollments = ctx.RegisterTable(EnrollmentsLogicalTable);

        await ctx.ExecuteAsync(
            $"""
             CREATE TABLE IF NOT EXISTS {ClassRooms.PhysicalName} (
                 class_room_id UUID PRIMARY KEY,
                 name TEXT NOT NULL,
                 max_students INT NOT NULL,
                 enrolled_count INT NOT NULL DEFAULT 0,
                 _last_sortable_unique_id TEXT NOT NULL,
                 _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
             );
             """,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ctx.ExecuteAsync(
            $"""
             CREATE TABLE IF NOT EXISTS {Students.PhysicalName} (
                 student_id UUID PRIMARY KEY,
                 name TEXT NOT NULL,
                 max_class_count INT NOT NULL,
                 enrolled_count INT NOT NULL DEFAULT 0,
                 _last_sortable_unique_id TEXT NOT NULL,
                 _last_applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
             );
             """,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ctx.ExecuteAsync(
            $"""
             CREATE TABLE IF NOT EXISTS {Enrollments.PhysicalName} (
                 student_id UUID NOT NULL,
                 class_room_id UUID NOT NULL,
                 enrolled_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                 _last_sortable_unique_id TEXT NOT NULL,
                 PRIMARY KEY (student_id, class_room_id)
             );
             """,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await ctx.ExecuteAsync(
            $"""
             CREATE INDEX IF NOT EXISTS {BuildIndexName(Enrollments.PhysicalName, "class_room")}
             ON {Enrollments.PhysicalName} (class_room_id);
             """,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // PostgreSQL identifier length limit is 63 bytes. The materialized view runtime already
    // truncates physical table names to fit, but a naive `idx_{table}_{col}` template can still
    // overflow once the prefix and suffix are added. Build a name that fits by shortening the
    // table portion and appending a short stable hash so it stays unique.
    private static string BuildIndexName(string physicalTable, string suffix)
    {
        const int maxLength = 63;
        var prefix = "idx_";
        var tail = "_" + suffix;
        var available = maxLength - prefix.Length - tail.Length;

        if (physicalTable.Length <= available)
        {
            return prefix + physicalTable + tail;
        }

        // Reserve 9 chars for "_" + 8-char hex hash so the truncated table name still has room.
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(physicalTable))).Substring(0, 8).ToLowerInvariant();
        var headroom = available - 9;
        if (headroom < 1) headroom = 1;
        var head = physicalTable.Substring(0, Math.Min(headroom, physicalTable.Length));
        return prefix + head + "_" + hash + tail;
    }

    public Task<IReadOnlyList<MvSqlStatement>> ApplyToViewAsync(
        Event ev,
        IMvApplyContext ctx,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MvSqlStatement>>(
            ev.Payload switch
            {
                ClassRoomCreated created => [InsertClassRoom(created, ctx.CurrentSortableUniqueId)],
                StudentCreated created => [InsertStudent(created, ctx.CurrentSortableUniqueId)],
                StudentEnrolledInClassRoom enrolled => InsertEnrollment(enrolled, ctx.CurrentSortableUniqueId),
                StudentDroppedFromClassRoom dropped => DeleteEnrollment(dropped, ctx.CurrentSortableUniqueId),
                _ => []
            });

    private MvSqlStatement InsertClassRoom(ClassRoomCreated created, string sortableUniqueId) =>
        new(
            $"""
             INSERT INTO {ClassRooms.PhysicalName}
                 (class_room_id, name, max_students, enrolled_count, _last_sortable_unique_id, _last_applied_at)
             VALUES
                 (@ClassRoomId, @Name, @MaxStudents, 0, @SortableUniqueId, NOW())
             ON CONFLICT (class_room_id) DO UPDATE SET
                 name = EXCLUDED.name,
                 max_students = EXCLUDED.max_students,
                 _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id,
                 _last_applied_at = EXCLUDED._last_applied_at
             WHERE {ClassRooms.PhysicalName}._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;
             """,
            new
            {
                created.ClassRoomId,
                created.Name,
                created.MaxStudents,
                SortableUniqueId = sortableUniqueId
            });

    private MvSqlStatement InsertStudent(StudentCreated created, string sortableUniqueId) =>
        new(
            $"""
             INSERT INTO {Students.PhysicalName}
                 (student_id, name, max_class_count, enrolled_count, _last_sortable_unique_id, _last_applied_at)
             VALUES
                 (@StudentId, @Name, @MaxClassCount, 0, @SortableUniqueId, NOW())
             ON CONFLICT (student_id) DO UPDATE SET
                 name = EXCLUDED.name,
                 max_class_count = EXCLUDED.max_class_count,
                 _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id,
                 _last_applied_at = EXCLUDED._last_applied_at
             WHERE {Students.PhysicalName}._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;
             """,
            new
            {
                created.StudentId,
                created.Name,
                created.MaxClassCount,
                SortableUniqueId = sortableUniqueId
            });

    private IReadOnlyList<MvSqlStatement> InsertEnrollment(
        StudentEnrolledInClassRoom enrolled,
        string sortableUniqueId) =>
    [
        new(
            $"""
             INSERT INTO {Enrollments.PhysicalName}
                 (student_id, class_room_id, enrolled_at, _last_sortable_unique_id)
             VALUES
                 (@StudentId, @ClassRoomId, NOW(), @SortableUniqueId)
             ON CONFLICT (student_id, class_room_id) DO UPDATE SET
                 _last_sortable_unique_id = EXCLUDED._last_sortable_unique_id
             WHERE {Enrollments.PhysicalName}._last_sortable_unique_id < EXCLUDED._last_sortable_unique_id;
             """,
            new
            {
                enrolled.StudentId,
                enrolled.ClassRoomId,
                SortableUniqueId = sortableUniqueId
            }),
        RecountClassRoom(enrolled.ClassRoomId, sortableUniqueId),
        RecountStudent(enrolled.StudentId, sortableUniqueId)
    ];

    private IReadOnlyList<MvSqlStatement> DeleteEnrollment(
        StudentDroppedFromClassRoom dropped,
        string sortableUniqueId) =>
    [
        new(
            $"""
             DELETE FROM {Enrollments.PhysicalName}
             WHERE student_id = @StudentId
               AND class_room_id = @ClassRoomId
               AND _last_sortable_unique_id < @SortableUniqueId;
             """,
            new
            {
                dropped.StudentId,
                dropped.ClassRoomId,
                SortableUniqueId = sortableUniqueId
            }),
        RecountClassRoom(dropped.ClassRoomId, sortableUniqueId),
        RecountStudent(dropped.StudentId, sortableUniqueId)
    ];

    private MvSqlStatement RecountClassRoom(Guid classRoomId, string sortableUniqueId) =>
        new(
            $"""
             UPDATE {ClassRooms.PhysicalName}
             SET enrolled_count = (
                     SELECT COUNT(*) FROM {Enrollments.PhysicalName}
                     WHERE class_room_id = @ClassRoomId
                 ),
                 _last_sortable_unique_id = @SortableUniqueId,
                 _last_applied_at = NOW()
             WHERE class_room_id = @ClassRoomId
               AND _last_sortable_unique_id < @SortableUniqueId;
             """,
            new
            {
                ClassRoomId = classRoomId,
                SortableUniqueId = sortableUniqueId
            });

    private MvSqlStatement RecountStudent(Guid studentId, string sortableUniqueId) =>
        new(
            $"""
             UPDATE {Students.PhysicalName}
             SET enrolled_count = (
                     SELECT COUNT(*) FROM {Enrollments.PhysicalName}
                     WHERE student_id = @StudentId
                 ),
                 _last_sortable_unique_id = @SortableUniqueId,
                 _last_applied_at = NOW()
             WHERE student_id = @StudentId
               AND _last_sortable_unique_id < @SortableUniqueId;
             """,
            new
            {
                StudentId = studentId,
                SortableUniqueId = sortableUniqueId
            });
}

public sealed class ClassRoomMvRow
{
    [MvColumn("class_room_id")]
    public Guid ClassRoomId { get; set; }

    [MvColumn("name")]
    public string Name { get; set; } = string.Empty;

    [MvColumn("max_students")]
    public int MaxStudents { get; set; }

    [MvColumn("enrolled_count")]
    public int EnrolledCount { get; set; }

    [MvColumn("_last_sortable_unique_id")]
    public string LastSortableUniqueId { get; set; } = string.Empty;

    [MvColumn("_last_applied_at")]
    public DateTimeOffset LastAppliedAt { get; set; }
}

public sealed class StudentMvRow
{
    [MvColumn("student_id")]
    public Guid StudentId { get; set; }

    [MvColumn("name")]
    public string Name { get; set; } = string.Empty;

    [MvColumn("max_class_count")]
    public int MaxClassCount { get; set; }

    [MvColumn("enrolled_count")]
    public int EnrolledCount { get; set; }

    [MvColumn("_last_sortable_unique_id")]
    public string LastSortableUniqueId { get; set; } = string.Empty;

    [MvColumn("_last_applied_at")]
    public DateTimeOffset LastAppliedAt { get; set; }
}

public sealed class EnrollmentMvRow
{
    [MvColumn("student_id")]
    public Guid StudentId { get; set; }

    [MvColumn("class_room_id")]
    public Guid ClassRoomId { get; set; }

    [MvColumn("enrolled_at")]
    public DateTimeOffset EnrolledAt { get; set; }

    [MvColumn("_last_sortable_unique_id")]
    public string LastSortableUniqueId { get; set; } = string.Empty;
}
