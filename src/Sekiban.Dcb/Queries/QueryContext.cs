using Microsoft.Extensions.DependencyInjection;

namespace Sekiban.Dcb.Queries;

/// <summary>
/// Default implementation of query context
/// </summary>
public class QueryContext : IQueryContext
{
    public IServiceProvider ServiceProvider { get; }
    
    public QueryContext(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }
    
    public T GetService<T>() where T : notnull
    {
        return ServiceProvider.GetRequiredService<T>();
    }
    
    public T? GetServiceOrDefault<T>() where T : class
    {
        return ServiceProvider.GetService<T>();
    }
}