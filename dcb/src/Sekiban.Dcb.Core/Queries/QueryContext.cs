using Microsoft.Extensions.DependencyInjection;
namespace Sekiban.Dcb.Queries;

/// <summary>
///     Default implementation of query context
/// </summary>
public class QueryContext : IQueryContext
{
    public QueryContext(
        IServiceProvider serviceProvider,
        int? safeVersion = null,
        string? safeWindowThreshold = null,
        DateTime? safeWindowThresholdTime = null,
        int? unsafeVersion = null)
    {
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        SafeVersion = safeVersion;
        SafeWindowThreshold = safeWindowThreshold;
        SafeWindowThresholdTime = safeWindowThresholdTime;
        UnsafeVersion = unsafeVersion;
    }

    public IServiceProvider ServiceProvider { get; }
    public int? SafeVersion { get; }
    public string? SafeWindowThreshold { get; }
    public DateTime? SafeWindowThresholdTime { get; }
    public int? UnsafeVersion { get; }

    public T GetService<T>() where T : notnull => ServiceProvider.GetRequiredService<T>();

    public T? GetServiceOrDefault<T>() where T : class => ServiceProvider.GetService<T>();
}
