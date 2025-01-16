using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
namespace Pure.Domain;

public record UserExistsQueryFromAggregateListProjection(string Email)
    : IMultiProjectionQuery<AggregateListProjector<UserProjector>, UserExistsQueryFromAggregateListProjection, bool>
{

    public static ResultBox<bool> HandleQuery(
        MultiProjectionState<AggregateListProjector<UserProjector>> projection,
        UserExistsQueryFromAggregateListProjection query,
        IQueryContext context) =>
        projection.Payload.Aggregates.Values.Any(user => GetEmailFromAggregate(user) == query.Email);

    private static OptionalValue<string> GetEmailFromAggregate(Aggregate aggregate) => aggregate.GetPayload() switch
    {
        ConfirmedUser confirmedUser => confirmedUser.Email,
        UnconfirmedUser unconfirmedUser => unconfirmedUser.Email,
        _ => OptionalValue<string>.Empty
    };
}
