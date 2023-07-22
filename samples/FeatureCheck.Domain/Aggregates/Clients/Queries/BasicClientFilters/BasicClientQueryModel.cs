using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.Clients.Queries.BasicClientFilters;

public record BasicClientQueryModel(Guid BranchId, string ClientName, string ClientEmail) : IQueryResponse;
