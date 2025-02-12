using ResultBoxes;
using Sekiban.Pure.Events;
namespace Sekiban.Pure.OrleansEventSourcing;

public interface IEventReader
{
    Task<ResultBox<IReadOnlyList<IEvent>>> GetEvents(EventRetrievalInfo eventRetrievalInfo);
}