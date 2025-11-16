namespace Sekiban.Dcb.Actors;

/// <summary>
/// Source of events delivered to the multi-projection actor.
/// </summary>
public enum EventSource
{
    Unknown = 0,
    Stream = 1,
    CatchUp = 2
}

