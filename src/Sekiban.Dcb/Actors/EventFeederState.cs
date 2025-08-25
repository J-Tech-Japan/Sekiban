namespace Sekiban.Dcb.Actors;

/// <summary>
///     State of the event feeder
/// </summary>
public enum EventFeederState
{
    /// <summary>
    ///     Not started
    /// </summary>
    NotStarted,

    /// <summary>
    ///     Catching up on historical events
    /// </summary>
    CatchingUp,

    /// <summary>
    ///     Processing live events
    /// </summary>
    Live,

    /// <summary>
    ///     Paused
    /// </summary>
    Paused,

    /// <summary>
    ///     Stopped
    /// </summary>
    Stopped,

    /// <summary>
    ///     Error state
    /// </summary>
    Error
}
