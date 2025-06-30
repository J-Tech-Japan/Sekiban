using Orleans;
using ResultBoxes;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;

namespace DaprSample.Domain.User.Queries;

[GenerateSerializer]
public record UserQuery(Guid UserId)
    : IMultiProjectionQuery<AggregateListProjector<UserProjector>, UserQuery, UserQuery.UserDetail>,
      IWaitForSortableUniqueId
{
    public string? WaitForSortableUniqueId { get; set; }
    
    public static ResultBox<UserDetail> HandleQuery(
        MultiProjectionState<AggregateListProjector<UserProjector>> projection, 
        UserQuery query, 
        IQueryContext context)
    {
        var userAggregate = projection.Payload.Aggregates
            .Where(m => m.Key.AggregateId == query.UserId)
            .Select(m => m.Value)
            .FirstOrDefault();

        if (userAggregate == null || userAggregate.Version == 0)
        {
            return ResultBox<UserDetail>.FromException(new InvalidOperationException($"User {query.UserId} not found"));
        }

        var payload = userAggregate.GetPayload();
        if (payload is not User user)
        {
            return ResultBox<UserDetail>.FromException(new InvalidOperationException($"User {query.UserId} payload is not of type User"));
        }

        return ResultBox<UserDetail>.FromValue(new UserDetail(
            userAggregate.PartitionKeys.AggregateId,
            user.Name,
            user.Email,
            userAggregate.Version,
            userAggregate.LastSortableUniqueId
        ));
    }

    [GenerateSerializer]
    public record UserDetail(
        Guid UserId,
        string Name,
        string Email,
        int Version,
        string LastEventId
    );
}