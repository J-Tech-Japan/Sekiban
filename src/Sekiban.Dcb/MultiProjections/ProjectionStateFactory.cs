namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Helper factory methods for creating projection states with different key types
/// </summary>
public static class ProjectionStateFactory
{
    /// <summary>
    ///     Create a GUID-keyed projection state (default)
    /// </summary>
    public static SafeUnsafeProjectionState<Guid, T> CreateGuidKeyed<T>() where T : class
    {
        return new SafeUnsafeProjectionState<Guid, T>();
    }

    /// <summary>
    ///     Create a string-keyed projection state
    /// </summary>
    public static SafeUnsafeProjectionState<string, T> CreateStringKeyed<T>() where T : class
    {
        return new SafeUnsafeProjectionState<string, T>();
    }

    /// <summary>
    ///     Create a composite-keyed projection state
    /// </summary>
    public static SafeUnsafeProjectionState<(string Category, Guid Id), T> CreateCompositeKeyed<T>() where T : class
    {
        return new SafeUnsafeProjectionState<(string Category, Guid Id), T>();
    }

    /// <summary>
    ///     Create an int-keyed projection state
    /// </summary>
    public static SafeUnsafeProjectionState<int, T> CreateIntKeyed<T>() where T : class
    {
        return new SafeUnsafeProjectionState<int, T>();
    }

    /// <summary>
    ///     Create a long-keyed projection state
    /// </summary>
    public static SafeUnsafeProjectionState<long, T> CreateLongKeyed<T>() where T : class
    {
        return new SafeUnsafeProjectionState<long, T>();
    }
}