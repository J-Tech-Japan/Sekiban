using ResultBoxes;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
namespace Pure.Domain;

public record
    UserQueryFromMultiProjection : IMultiProjectionListQuery<MultiProjectorPayload, UserQueryFromMultiProjection,
    string>
{
    public static ResultBox<IEnumerable<string>> HandleFilter(
        MultiProjectionState<MultiProjectorPayload> projection,
        UserQueryFromMultiProjection query,
        IQueryContext context) =>
        projection.Payload.Users.Values.Select(user => user.Name).ToList();
    public static ResultBox<IEnumerable<string>> HandleSort(
        IEnumerable<string> filteredList,
        UserQueryFromMultiProjection query,
        IQueryContext context) =>
        filteredList.OrderBy(name => name).ToList();
}
