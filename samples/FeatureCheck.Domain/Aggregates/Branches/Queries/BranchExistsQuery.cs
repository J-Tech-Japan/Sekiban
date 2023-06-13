using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace FeatureCheck.Domain.Aggregates.Branches.Queries;

public class BranchExistsQuery : IAggregateQuery<Branch, BranchExistsQuery.Parameter, BranchExistsQuery.Response>
{

    public Response HandleFilter(Parameter param, IEnumerable<AggregateState<Branch>> list)
    {
        return new Response(list.Any(b => b.AggregateId == param.BranchId));
    }
    public record Parameter(Guid BranchId) : IQueryParameter<Response>;

    public record Response(bool Exists) : IQueryResponse;
}
