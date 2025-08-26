using Dcb.Domain.Student;
using Orleans;
using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
namespace Dcb.Domain.Queries;

[GenerateSerializer]
public record GetStudentListQuery :
    IMultiProjectionListQuery<GenericTagMultiProjector<StudentProjector, StudentTag>, GetStudentListQuery, StudentState>,
    IWaitForSortableUniqueId,
    IQueryPagingParameter
{
    // Paging parameters (from IQueryPagingParameter)
    [Id(0)]
    public int? PageNumber { get; init; }
    [Id(1)]
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

    // Wait for sortable unique ID (from IWaitForSortableUniqueId)
    [Id(2)]
    public string? WaitForSortableUniqueId { get; init; }
}