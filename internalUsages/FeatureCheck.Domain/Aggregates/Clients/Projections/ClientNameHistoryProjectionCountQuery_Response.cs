using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.Clients.Projections;

public record ClientNameHistoryProjectionCountQuery_Response(int Count) : IQueryResponse;
