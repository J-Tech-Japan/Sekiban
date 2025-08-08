using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.InMemory;

public class InMemoryStreamDestinationResolver : IStreamDestinationResolver
{
    private readonly ISekibanStream _stream;

    public InMemoryStreamDestinationResolver(ISekibanStream stream)
    {
        _stream = stream;
    }

    public IEnumerable<ISekibanStream> Resolve(Event evt, IReadOnlyCollection<ITag> tags)
    {
        yield return _stream;
    }
}
