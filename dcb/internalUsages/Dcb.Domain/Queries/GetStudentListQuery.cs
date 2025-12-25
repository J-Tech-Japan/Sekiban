using Dcb.Domain.Student;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
namespace Dcb.Domain.Queries;

public record GetStudentListQuery :
    IMultiProjectionListQuery<GenericTagMultiProjector<StudentProjector, StudentTag>, GetStudentListQuery, StudentState>,
    IWaitForSortableUniqueId,
    IQueryPagingParameter
{
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }

    // Required static methods for IMultiProjectionListQuery
    public static ResultBox<IEnumerable<StudentState>> HandleFilter(
        GenericTagMultiProjector<StudentProjector, StudentTag> projector,
        GetStudentListQuery query,
        IQueryContext context)
    {
        // Get all state payloads from the projector and filter to StudentState
        var students = projector.GetStatePayloads()
            .OfType<StudentState>()
            .AsEnumerable();

        return ResultBox.FromValue(students);
    }

    public static ResultBox<IEnumerable<StudentState>> HandleSort(
        IEnumerable<StudentState> filteredList,
        GetStudentListQuery query,
        IQueryContext context)
    {
        return ResultBox.FromValue(filteredList.OrderBy(s => s.Name).AsEnumerable());
    }

    public string? WaitForSortableUniqueId { get; init; }
}