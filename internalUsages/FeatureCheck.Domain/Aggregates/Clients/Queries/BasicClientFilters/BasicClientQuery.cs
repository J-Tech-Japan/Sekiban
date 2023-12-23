using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Aggregates.Clients.Queries.BasicClientFilters;

// ReSharper disable once ClassNeverInstantiated.Global
public class BasicClientQuery : IAggregateListQuery<Client, BasicClientQueryParameter, BasicClientQueryModel>
{
    public IEnumerable<BasicClientQueryModel> HandleFilter(BasicClientQueryParameter queryParam, IEnumerable<AggregateState<Client>> list)
    {
        return list.Where(m => queryParam.BranchId is null || m.Payload.BranchId == queryParam.BranchId)
            .Select(m => new BasicClientQueryModel(m.Payload.BranchId, m.Payload.ClientName, m.Payload.ClientEmail));
    }

    public IEnumerable<BasicClientQueryModel> HandleSort(BasicClientQueryParameter queryParam, IEnumerable<BasicClientQueryModel> filteredList)
    {
        var sort = new Dictionary<BasicClientQuerySortKey, bool>();
        if (queryParam.SortKey1 != null)
        {
            sort.Add(queryParam.SortKey1.Value, queryParam.SortKey1Asc ?? true);
        }
        if (queryParam.SortKey2 != null)
        {
            sort.Add(queryParam.SortKey2.Value, queryParam.SortKey2Asc ?? true);
        }
        if (sort.Count == 0)
        {
            return filteredList.OrderBy(m => m.ClientName).ThenBy(m => m.ClientEmail);
        }
        var result = filteredList;
        foreach (var (sortKey, index) in sort.Select((item, index) => (item, index)))
        {
            if (index == 0)
            {
                result = sortKey.Value
                    ? result.OrderBy(
                        m => sortKey.Key switch
                        {
                            BasicClientQuerySortKey.Name => m.ClientName,
                            BasicClientQuerySortKey.Email => m.ClientEmail,
                            _ => throw new ArgumentOutOfRangeException()
                        })
                    : result.OrderByDescending(
                        m => sortKey.Key switch
                        {
                            BasicClientQuerySortKey.Name => m.ClientName,
                            BasicClientQuerySortKey.Email => m.ClientEmail,
                            _ => throw new ArgumentOutOfRangeException()
                        });
            } else
            {
                result = sortKey.Value
                    ? (result as IOrderedEnumerable<BasicClientQueryModel> ?? throw new InvalidCastException()).ThenBy(
                        m => sortKey.Key switch
                        {
                            BasicClientQuerySortKey.Name => m.ClientName,
                            BasicClientQuerySortKey.Email => m.ClientEmail,
                            _ => throw new ArgumentOutOfRangeException()
                        })
                    : (result as IOrderedEnumerable<BasicClientQueryModel> ?? throw new InvalidCastException()).ThenByDescending(
                        m => sortKey.Key switch
                        {
                            BasicClientQuerySortKey.Name => m.ClientName,
                            BasicClientQuerySortKey.Email => m.ClientEmail,
                            _ => throw new ArgumentOutOfRangeException()
                        });
            }
        }
        return result;
    }
}
