using FeatureCheck.Domain.Aggregates.Clients.Queries;
using Sekiban.Core.Query.QueryModel;
namespace FeatureCheck.Domain.Common;

public class DependencyInjectionSampleService
{
    private readonly IQueryExecutor _queryExecutor;

    public DependencyInjectionSampleService(IQueryExecutor queryExecutor) => _queryExecutor = queryExecutor;

    public async Task<bool> ExistsClientEmail(string clientEmail)
    {
        var result = await _queryExecutor.ExecuteAsync(new ClientEmailExistsQuery.Parameter(clientEmail));
        await Task.CompletedTask;
        return result.Exists;
    }
}
