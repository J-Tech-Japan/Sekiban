using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.InMemory;

public class InMemorySekibanStream : ISekibanStream
{
    private readonly string _topic;

    public InMemorySekibanStream(string topic = "events.all") => _topic = topic;

    public string GetTopic(Event evt) => _topic;
}
