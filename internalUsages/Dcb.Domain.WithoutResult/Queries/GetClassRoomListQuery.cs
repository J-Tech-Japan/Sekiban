using Dcb.Domain.WithoutResult.ClassRoom;
using Orleans;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
namespace Dcb.Domain.WithoutResult.Queries;

[GenerateSerializer]
public record GetClassRoomListQuery :
    IMultiProjectionListQueryWithoutResult<GenericTagMultiProjector<ClassRoomProjector, ClassRoomTag>, GetClassRoomListQuery, ClassRoomItem>,
    IWaitForSortableUniqueId,
    IQueryPagingParameter
{
    // Paging parameters (from IQueryPagingParameter)
    [Id(0)]
    public int? PageNumber { get; init; }
    [Id(1)]
    public int? PageSize { get; init; }

    // Required static methods for IMultiProjectionListQueryWithoutResult
    public static IEnumerable<ClassRoomItem> HandleFilter(
        GenericTagMultiProjector<ClassRoomProjector, ClassRoomTag> projector,
        GetClassRoomListQuery query,
        IQueryContext context)
    {
        return projector.GetStatePayloads()
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
    }

    public static IEnumerable<ClassRoomItem> HandleSort(
        IEnumerable<ClassRoomItem> filteredList,
        GetClassRoomListQuery query,
        IQueryContext context) =>
        filteredList.OrderBy(c => c.Name, StringComparer.Ordinal);

    // Wait for sortable unique ID (from IWaitForSortableUniqueId)
    [Id(2)]
    public string? WaitForSortableUniqueId { get; init; }
}
