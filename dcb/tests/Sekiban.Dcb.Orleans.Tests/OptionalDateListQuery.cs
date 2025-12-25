using ResultBoxes;
using Sekiban.Dcb.Queries;
namespace Sekiban.Dcb.Orleans.Tests;

[
    GenerateSerializer
]
public record OptionalDateListQuery
    : IMultiProjectionListQuery<TestOptionalDateMultiProjector, OptionalDateListQuery, OptionalDateResult>
{
    [Id(0)]
    public int? PageNumber { get; init; } = 1;
    [Id(1)]
    public int? PageSize { get; init; } = 10;

    public static ResultBox<IEnumerable<OptionalDateResult>> HandleFilter(
        TestOptionalDateMultiProjector projector,
        OptionalDateListQuery query,
        IQueryContext context) => ResultBox.FromValue(projector.Results.AsEnumerable());

    public static ResultBox<IEnumerable<OptionalDateResult>> HandleSort(
        IEnumerable<OptionalDateResult> filteredList,
        OptionalDateListQuery query,
        IQueryContext context) => ResultBox.FromValue(filteredList);
}