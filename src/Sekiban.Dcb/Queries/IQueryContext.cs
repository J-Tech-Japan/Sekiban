namespace Sekiban.Dcb.Queries;

/// <summary>
///     Context for query execution
/// </summary>
public interface IQueryContext
{
    /// <summary>
    ///     Service provider for dependency injection
    /// </summary>
    IServiceProvider ServiceProvider { get; }

    /// <summary>
    ///     Get a service from the context
    /// </summary>
    /// <typeparam name="T">The service type</typeparam>
    /// <returns>The service instance</returns>
    T GetService<T>() where T : notnull;

    /// <summary>
    ///     Try to get a service from the context
    /// </summary>
    /// <typeparam name="T">The service type</typeparam>
    /// <returns>The service instance or null</returns>
    T? GetServiceOrDefault<T>() where T : class;
}
