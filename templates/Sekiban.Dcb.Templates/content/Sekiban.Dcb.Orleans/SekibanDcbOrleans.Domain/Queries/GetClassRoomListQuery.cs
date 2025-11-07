using Dcb.Domain.ClassRoom;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
namespace Dcb.Domain.Queries;

public record GetClassRoomListQuery :
    IMultiProjectionListQuery<GenericTagMultiProjector<ClassRoomProjector, ClassRoomTag>, GetClassRoomListQuery, ClassRoomItem>,
    IWaitForSortableUniqueId,
    IQueryPagingParameter
{
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }

    // Required static methods for IMultiProjectionListQuery
    public static ResultBox<IEnumerable<ClassRoomItem>> HandleFilter(
        GenericTagMultiProjector<ClassRoomProjector, ClassRoomTag> projector,
        GetClassRoomListQuery query,
        IQueryContext context)
    {
        // Get all state payloads from the projector and convert to ClassRoomItem
        var classrooms = projector.GetStatePayloads()
            .Select(payload => payload switch
            {
                AvailableClassRoomState available => new ClassRoomItem
                {
                    ClassRoomId = available.ClassRoomId,
                    Name = available.Name,
                    MaxStudents = available.MaxStudents,
                    EnrolledCount = available.EnrolledStudentIds.Count,
                    IsFull = false,
                    RemainingCapacity = available.MaxStudents - available.EnrolledStudentIds.Count
                },
                FilledClassRoomState filled => new ClassRoomItem
                {
                    ClassRoomId = filled.ClassRoomId,
                    Name = filled.Name,
                    MaxStudents = filled.EnrolledStudentIds.Count, // When full, max equals enrolled
                    EnrolledCount = filled.EnrolledStudentIds.Count,
                    IsFull = true,
                    RemainingCapacity = 0
                },
                _ => null
            })
            .Where(item => item != null)
            .Cast<ClassRoomItem>()
            .AsEnumerable();

        return ResultBox.FromValue(classrooms);
    }

    public static ResultBox<IEnumerable<ClassRoomItem>> HandleSort(
        IEnumerable<ClassRoomItem> filteredList,
        GetClassRoomListQuery query,
        IQueryContext context)
    {
        return ResultBox.FromValue(filteredList.OrderBy(c => c.Name).AsEnumerable());
    }

    public string? WaitForSortableUniqueId { get; init; }
}
