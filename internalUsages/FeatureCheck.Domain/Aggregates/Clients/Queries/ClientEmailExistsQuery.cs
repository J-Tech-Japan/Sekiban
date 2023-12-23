using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.Clients.Queries;

public class ClientEmailExistsQuery : IAggregateQuery<Client, ClientEmailExistsQuery.Parameter, ClientEmailExistsQuery.Response>
{

    public Response HandleFilter(Parameter param, IEnumerable<AggregateState<Client>> list)
    {
        return new Response(list.Any(c => c.Payload.ClientEmail == param.Email));
    }

    public record Parameter(string Email) : IQueryParameter<Response>
    {
        public string RootPartitionKey { get; init; } = IDocument.DefaultRootPartitionKey;
        public string GetRootPartitionKey() => RootPartitionKey;
    }
    public record Response(bool Exists) : IQueryResponse;
}
