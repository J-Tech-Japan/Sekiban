using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Actors;

public interface IStreamDestinationResolver
{
    IEnumerable<ISekibanStream> Resolve(Event evt, IReadOnlyCollection<ITag> tags);
}