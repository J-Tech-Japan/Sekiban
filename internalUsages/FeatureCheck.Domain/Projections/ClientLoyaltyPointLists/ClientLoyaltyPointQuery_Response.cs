using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Projections.ClientLoyaltyPointLists;

public record ClientLoyaltyPointQuery_Response(
    Guid BranchId,
    string BranchName,
    Guid ClientId,
    string ClientName,
    int Point)
    : ClientLoyaltyPointListProjection.ClientLoyaltyPointListRecord(BranchId, BranchName, ClientId, ClientName, Point),
        IQueryResponse;
