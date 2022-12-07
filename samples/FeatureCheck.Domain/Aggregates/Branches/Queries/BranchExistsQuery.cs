using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Shared;
namespace FeatureCheck.Domain.Aggregates.Branches.Queries;

public class BranchExistsQuery : IAggregateQuery<Branch, BranchExistsQuery.QueryParameter, BranchExistsQuery.Response>
{
    private readonly ISekibanDateProducer _dateProducer;

    public BranchExistsQuery(ISekibanDateProducer dateProducer) => _dateProducer = dateProducer;

    public Response HandleFilter(QueryParameter queryParam, IEnumerable<AggregateState<Branch>> list)
    {
        return new Response(list.Any(b => b.AggregateId == queryParam.BranchId));
    }
    public record QueryParameter(Guid BranchId) : IQueryParameter<Response>;

    public record Response(bool Exists) : IQueryResponse;
}
