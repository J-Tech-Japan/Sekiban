using Dapr.Actors;

namespace DaprSample2;

/// <summary>
/// Counter Actor interface
/// </summary>
public interface ICounterActor : IActor
{
    /// <summary>
    /// Get counter value
    /// </summary>
    Task<int> GetCountAsync();

    /// <summary>
    /// Increment counter
    /// </summary>
    Task IncrementAsync();

    /// <summary>
    /// Reset counter
    /// </summary>
    Task ResetAsync();
}
