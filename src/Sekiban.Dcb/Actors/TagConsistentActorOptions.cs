namespace Sekiban.Dcb.Actors;

/// <summary>
/// Configuration options for TagConsistent actors
/// </summary>
public class TagConsistentActorOptions
{
    /// <summary>
    /// The time window in seconds during which a reservation can be cancelled
    /// after it has been confirmed. Default is 30 seconds.
    /// </summary>
    public double CancellationWindowSeconds { get; set; } = 30.0;
}