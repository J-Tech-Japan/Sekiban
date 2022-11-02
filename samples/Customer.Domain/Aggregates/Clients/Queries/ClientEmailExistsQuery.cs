using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Shared;
namespace Customer.Domain.Aggregates.Clients.Queries;

public class ClientEmailExistsQuery : IAggregateQuery<Client, ClientEmailExistsQuery.QueryParameter, bool>
{
    private readonly ISekibanDateProducer _dateProducer;
    public ClientEmailExistsQuery(ISekibanDateProducer dateProducer) => _dateProducer = dateProducer;
    public bool HandleFilter(QueryParameter queryParam, IEnumerable<AggregateIdentifierState<Client>> list)
    {
        return list.Any(c => c.Payload.ClientEmail == queryParam.Email);
    }
    public bool HandleSort(QueryParameter queryParam, bool projections) => projections;
    public record QueryParameter(string Email) : IQueryParameter;
}
