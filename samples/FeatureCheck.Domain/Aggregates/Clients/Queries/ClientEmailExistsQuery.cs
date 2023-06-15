using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace FeatureCheck.Domain.Aggregates.Clients.Queries;

public class ClientEmailExistsQuery : IAggregateQuery<Client, ClientEmailExistsQuery.Parameter, ClientEmailExistsQuery.Response>
{

    public Response HandleFilter(Parameter param, IEnumerable<AggregateState<Client>> list)
    {
        return new Response(list.Any(c => c.Payload.ClientEmail == param.Email));
    }

    public record Parameter(string Email) : IQueryParameter<Response>;
    public record Response(bool Exists) : IQueryResponse;
}
