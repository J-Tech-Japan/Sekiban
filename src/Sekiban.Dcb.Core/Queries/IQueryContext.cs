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

    /// <summary>
    ///     Safe projection version (number of events incorporated into safe state)
    /// </summary>
    int? SafeVersion { get; }

    /// <summary>
    ///     Safe window threshold SortableUniqueId (string value)
    /// </summary>
    string? SafeWindowThreshold { get; }

    /// <summary>
    ///     Safe window threshold time (derived from SortableUniqueId)
    /// </summary>
    DateTime? SafeWindowThresholdTime { get; }

    /// <summary>
    ///     Current (unsafe) projection version including events not yet safe
    /// </summary>
    int? UnsafeVersion { get; }
}
