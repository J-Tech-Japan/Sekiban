using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
using Sekiban.Core.Query.QueryModel.Parameters;
namespace Customer.Domain.Aggregates.Clients.QueryFilters.BasicClientFilters;

public enum BasicClientQueryFilterSortKey
{
    Name, Email
}
// ReSharper disable once ClassNeverInstantiated.Global
public record BasicClientQueryFilterParameter(
    Guid? BranchId,
    int? PageSize,
    int? PageNumber,
    BasicClientQueryFilterSortKey? SortKey1,
    BasicClientQueryFilterSortKey? SortKey2,
    bool? SortKey1Asc,
    bool? SortKey2Asc) : IQueryPagingParameter;
public record BasicClientQueryModel(Guid BranchId, string ClientName, string ClientEmail);
public class BasicClientQueryFilter : IAggregateListQuery<Client, BasicClientQueryFilterParameter,
    BasicClientQueryModel>
{
    public IEnumerable<BasicClientQueryModel> HandleFilter(BasicClientQueryFilterParameter queryParam, IEnumerable<AggregateState<Client>> list)
    {
        return list.Where(m => queryParam.BranchId is null || m.Payload.BranchId == queryParam.BranchId)
            .Select(m => new BasicClientQueryModel(m.Payload.BranchId, m.Payload.ClientName, m.Payload.ClientEmail));
    }
    public IEnumerable<BasicClientQueryModel> HandleSort(BasicClientQueryFilterParameter queryParam, IEnumerable<BasicClientQueryModel> projections)
    {
        var sort = new Dictionary<BasicClientQueryFilterSortKey, bool>();
        if (queryParam.SortKey1 != null) { sort.Add(queryParam.SortKey1.Value, queryParam.SortKey1Asc ?? true); }
        if (queryParam.SortKey2 != null) { sort.Add(queryParam.SortKey2.Value, queryParam.SortKey2Asc ?? true); }
        if (sort.Count == 0)
        {
            return projections.OrderBy(m => m.ClientName).ThenBy(m => m.ClientEmail);
        }
        var result = projections;
        foreach (var (sortKey, index) in sort.Select((item, index) => (item, index)))
        {
            if (index == 0)
            {
                result = sortKey.Value
                    ? result.OrderBy(
                        m => sortKey.Key switch
                        {
                            BasicClientQueryFilterSortKey.Name => m.ClientName,
                            BasicClientQueryFilterSortKey.Email => m.ClientEmail,
                            _ => throw new ArgumentOutOfRangeException()
                        })
                    : result.OrderByDescending(
                        m => sortKey.Key switch
                        {
                            BasicClientQueryFilterSortKey.Name => m.ClientName,
                            BasicClientQueryFilterSortKey.Email => m.ClientEmail,
                            _ => throw new ArgumentOutOfRangeException()
                        });
            }
            else
            {
                result = sortKey.Value
                    ? (result as IOrderedEnumerable<BasicClientQueryModel> ?? throw new InvalidCastException()).ThenBy(
                        m => sortKey.Key switch
                        {
                            BasicClientQueryFilterSortKey.Name => m.ClientName,
                            BasicClientQueryFilterSortKey.Email => m.ClientEmail,
                            _ => throw new ArgumentOutOfRangeException()
                        })
                    : (result as IOrderedEnumerable<BasicClientQueryModel> ?? throw new InvalidCastException()).ThenByDescending(
                        m => sortKey.Key switch
                        {
                            BasicClientQueryFilterSortKey.Name => m.ClientName,
                            BasicClientQueryFilterSortKey.Email => m.ClientEmail,
                            _ => throw new ArgumentOutOfRangeException()
                        });
            }
        }
        return result;
    }
}
