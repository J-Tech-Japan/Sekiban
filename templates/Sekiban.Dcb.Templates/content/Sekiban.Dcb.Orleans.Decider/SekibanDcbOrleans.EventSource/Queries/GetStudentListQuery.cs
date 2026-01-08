using Dcb.EventSource.Student;
using Dcb.ImmutableModels.States.Student;
using Dcb.ImmutableModels.Tags;
using Orleans;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
namespace Dcb.EventSource.Queries;

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
    public static IEnumerable<StudentState> HandleFilter(
        GenericTagMultiProjector<StudentProjector, StudentTag> projector,
        GetStudentListQuery query,
        IQueryContext context)
    {
        return projector.GetStatePayloads()
            .OfType<StudentState>()
            .AsEnumerable();
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
