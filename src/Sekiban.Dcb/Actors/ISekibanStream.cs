using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Actors;

/// <summary>
/// Resolves stream/topic for events.
/// </summary>
public interface ISekibanStream
{
    string GetTopic(Event evt);
}