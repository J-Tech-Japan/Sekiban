using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Shared;
namespace FeatureCheck.Domain.Aggregates.Branches.Queries;

public class BranchExistsQuery : IAggregateQuery<Branch, BranchExistsQuery.QueryParameter, BranchExistsQuery.Output>
{
    private readonly ISekibanDateProducer _dateProducer;

    public BranchExistsQuery(ISekibanDateProducer dateProducer) => _dateProducer = dateProducer;

    public Output HandleFilter(QueryParameter queryParam, IEnumerable<AggregateState<Branch>> list)
    {
        return new Output(list.Any(b => b.AggregateId == queryParam.BranchId));
    }
    public record QueryParameter(Guid BranchId) : IQueryParameterCommon, IQueryInput<Output>;

    public record Output(bool Exists) : IQueryOutput;
}
