namespace Sekiban.Dcb.Actors;

/// <summary>
///     State of the event provider
/// </summary>
public enum EventProviderState
{
    NotStarted,
    CatchingUp,
    Live,
    Paused,
    Stopped,
    Error
}