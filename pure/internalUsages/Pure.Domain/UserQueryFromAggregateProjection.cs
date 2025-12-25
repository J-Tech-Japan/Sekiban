using ResultBoxes;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
namespace Pure.Domain;

public record UserQueryFromAggregateProjection(string PartOfName)
    : IMultiProjectionListQuery<AggregateListProjector<UserProjector>, UserQueryFromAggregateProjection, string>
{
    public static ResultBox<IEnumerable<string>> HandleFilter(
        MultiProjectionState<AggregateListProjector<UserProjector>> projection,
        UserQueryFromAggregateProjection query,
        IQueryContext context) =>
        projection
            .Payload
            .Aggregates
            .Values
            .Where(user => user.Payload is ConfirmedUser)
            .Select(user => user.ToTypedPayload<ConfirmedUser>())
            .Where(payload => payload.IsSuccess)
            .Select(payload => payload.GetValue())
            .Where(user => string.IsNullOrWhiteSpace(query.PartOfName) || user.Payload.Name.Contains(query.PartOfName))
            .Select(user => user.Payload.Name)
            .ToList();
    public static ResultBox<IEnumerable<string>> HandleSort(
        IEnumerable<string> filteredList,
        UserQueryFromAggregateProjection query,
        IQueryContext context) => filteredList.OrderBy(name => name).ToList();
}
