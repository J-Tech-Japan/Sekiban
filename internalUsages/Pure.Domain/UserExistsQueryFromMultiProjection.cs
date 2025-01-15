using ResultBoxes;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
namespace Pure.Domain;

public record UserExistsQueryFromMultiProjection(string Email)
    : IMultiProjectionQuery<MultiProjectorPayload, UserExistsQueryFromMultiProjection, bool>
{
    public static ResultBox<bool> HandleQuery(
        MultiProjectionState<MultiProjectorPayload> projection,
        UserExistsQueryFromMultiProjection query,
        IQueryContext context) =>
        projection.Payload.Users.Values.Any(user => user.Email == query.Email);
}