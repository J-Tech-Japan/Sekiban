using Microsoft.Extensions.DependencyInjection;
namespace Sekiban.Dcb.Queries;

/// <summary>
///     Default implementation of query context
/// </summary>
public class QueryContext : IQueryContext
{

    public QueryContext(IServiceProvider serviceProvider) =>
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    public IServiceProvider ServiceProvider { get; }

    public T GetService<T>() where T : notnull => ServiceProvider.GetRequiredService<T>();

    public T? GetServiceOrDefault<T>() where T : class => ServiceProvider.GetService<T>();
}
