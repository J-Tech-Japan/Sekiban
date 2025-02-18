using ResultBoxes;
namespace Sekiban.Pure.Events;

public interface IEventReader
{
    Task<ResultBox<IReadOnlyList<IEvent>>> GetEvents(EventRetrievalInfo eventRetrievalInfo);
}