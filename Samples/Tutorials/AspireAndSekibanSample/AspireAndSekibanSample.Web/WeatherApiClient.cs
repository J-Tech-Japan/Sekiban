using AspireAndSekibanSample.Domain.Aggregates.AccountUsers;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query;
using Sekiban.Core.Query.QueryModel;
namespace AspireAndSekibanSample.Web;

public class WeatherApiClient(HttpClient httpClient)
{
    public async Task<ListQueryResult<QueryAggregateState<AccountUser>>?> GetAccountUserAsync()
    {
        return await httpClient.GetFromJsonAsync<ListQueryResult<QueryAggregateState<AccountUser>>>("/api/query/accountuser/simpleaggregatelistquery1");
    }
}
