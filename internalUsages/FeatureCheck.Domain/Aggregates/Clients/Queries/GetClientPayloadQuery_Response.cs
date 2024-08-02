using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.Clients.Queries;

public record GetClientPayloadQuery_Response(Client Client, Guid ClientId, int Version) : IQueryResponse;
