using Orleans;

namespace Sekiban.Pure.Dapr.Serialization;

/// <summary>
/// Base class for Dapr surrogate pattern implementation
/// </summary>
/// <typeparam name="T">The type this surrogate represents</typeparam>
public abstract class DaprSurrogate<T>
{
    /// <summary>
    /// Converts the surrogate back to the original type
    /// </summary>
    /// <returns>The original object</returns>
    public abstract T ConvertFromSurrogate();

    /// <summary>
    /// Populates the surrogate with data from the original object
    /// </summary>
    /// <param name="value">The original object</param>
    public abstract void ConvertToSurrogate(T value);
}