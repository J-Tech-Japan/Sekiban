using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.TenantUsers;

public class TenantUserDuplicateEmailQuery : ITenantAggregateQuery<TenantUser, TenantUserDuplicateEmailQuery.Parameter,
    TenantUserDuplicateEmailQuery.Response>
{
    public Response HandleFilter(Parameter queryParam, IEnumerable<AggregateState<TenantUser>> list)
    {
        return list.Any(m => m.Payload.Email == queryParam.Email) ? new Response(true) : new Response(false);
    }

    public record Parameter(string TenantId, string Email) : ITenantQueryParameter<Response>
    {
        public string GetTenantId() => TenantId;
    }

    public record Response(bool IsDuplicate) : IQueryResponse;
}
