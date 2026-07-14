using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.InMemory;

[Obsolete(
    "Moved to Sekiban.Dcb.Core.Testing (namespace Sekiban.Dcb.Testing). This type is volatile/in-process and is for tests only; it lives in a production package for historical reasons, which is how it reached production once. Behaviour is unchanged and it will not be removed before the next major version.")]
public class InMemoryStreamDestinationResolver : IStreamDestinationResolver
{
    private readonly ISekibanStream _stream;

    public InMemoryStreamDestinationResolver(ISekibanStream stream) => _stream = stream;

    public IEnumerable<ISekibanStream> Resolve(Event evt, IReadOnlyCollection<ITag> tags)
    {
        yield return _stream;
    }
}
