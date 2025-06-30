using Orleans;
using ResultBoxes;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;

namespace DaprSample.Domain.User.Queries;

[GenerateSerializer]
public record UserListQuery(string NameContains = "", string EmailContains = "")
    : IMultiProjectionListQuery<AggregateListProjector<UserProjector>, UserListQuery, UserListQuery.UserRecord>,
      IWaitForSortableUniqueId
{
    public string? WaitForSortableUniqueId { get; set; }
    
    public static ResultBox<IEnumerable<UserRecord>> HandleFilter(
        MultiProjectionState<AggregateListProjector<UserProjector>> projection, 
        UserListQuery query, 
        IQueryContext context)
    {
        return projection.Payload.Aggregates
            .Select(m => (m.Value.GetPayload(), m.Value.PartitionKeys))
            .Where(tuple => tuple.Item1 is User)
            .Select(tuple => ((User)tuple.Item1, tuple.PartitionKeys))
            .Where(tuple => 
                (string.IsNullOrEmpty(query.NameContains) || 
                 tuple.Item1.Name.Contains(query.NameContains, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrEmpty(query.EmailContains) || 
                 tuple.Item1.Email.Contains(query.EmailContains, StringComparison.OrdinalIgnoreCase)))
            .Select(tuple => new UserRecord(
                tuple.PartitionKeys.AggregateId, 
                tuple.Item1.Name, 
                tuple.Item1.Email))
            .ToResultBox();
    }

    public static ResultBox<IEnumerable<UserRecord>> HandleSort(
        IEnumerable<UserRecord> filteredList, 
        UserListQuery query, 
        IQueryContext context)
    {
        return filteredList.OrderBy(m => m.Name).AsEnumerable().ToResultBox();
    }

    [GenerateSerializer]
    public record UserRecord(
        Guid UserId,
        string Name,
        string Email
    );
}