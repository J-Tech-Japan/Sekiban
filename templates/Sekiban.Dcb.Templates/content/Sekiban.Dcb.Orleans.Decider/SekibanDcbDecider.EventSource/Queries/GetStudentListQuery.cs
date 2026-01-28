using Dcb.ImmutableModels.States.Student;
using Dcb.EventSource.Projections;
using Orleans;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
namespace Dcb.EventSource.Queries;

public record GetStudentListQuery :
    IMultiProjectionListQuery<StudentListProjection, GetStudentListQuery, StudentState>,
    IWaitForSortableUniqueId,
    IQueryPagingParameter
{
    // Paging parameters (from IQueryPagingParameter)
    [Id(0)]
    public int? PageNumber { get; init; }
    [Id(1)]
    public int? PageSize { get; init; }

    // Required static methods for IMultiProjectionListQuery
    public static IEnumerable<StudentState> HandleFilter(
        StudentListProjection projector,
        GetStudentListQuery query,
        IQueryContext context)
    {
        return projector.GetAllStudents();
    }

    public static IEnumerable<StudentState> HandleSort(
        IEnumerable<StudentState> filteredList,
        GetStudentListQuery query,
        IQueryContext context) =>
        filteredList.OrderBy(s => s.Name, StringComparer.Ordinal);

    // Wait for sortable unique ID (from IWaitForSortableUniqueId)
    [Id(2)]
    public string? WaitForSortableUniqueId { get; init; }
}
