using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.InMemory;

[Obsolete(
    "Moved to Sekiban.Dcb.Core.Testing (namespace Sekiban.Dcb.Testing). This type is volatile/in-process and is for tests only; it lives in a production package for historical reasons, which is how it reached production once. Behaviour is unchanged and it will not be removed before the next major version.")]
public class InMemorySekibanStream : ISekibanStream
{
    private readonly string _topic;

    public InMemorySekibanStream(string topic = "events.all") => _topic = topic;

    public string GetTopic(Event evt) => _topic;
}
