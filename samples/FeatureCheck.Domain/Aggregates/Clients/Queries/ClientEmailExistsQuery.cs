using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Shared;
namespace Customer.Domain.Aggregates.Clients.Queries;

public class ClientEmailExistsQuery : IAggregateQuery<Client, ClientEmailExistsQuery.QueryParameter, ClientEmailExistsQuery.Output>
{
    private readonly ISekibanDateProducer _dateProducer;

    public ClientEmailExistsQuery(ISekibanDateProducer dateProducer) => _dateProducer = dateProducer;

    public Output HandleFilter(QueryParameter queryParam, IEnumerable<AggregateState<Client>> list)
    {
        return new Output(list.Any(c => c.Payload.ClientEmail == queryParam.Email));
    }

    public record QueryParameter(string Email) : IQueryParameter, IQueryInput<Output>;
    public record Output(bool Exists) : IQueryOutput;
}
