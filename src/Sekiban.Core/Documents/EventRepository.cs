using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
namespace Sekiban.Core.Documents;

public class EventRepository(
    IEventTemporaryRepository temporaryRepository,
    IEventPersistentRepository persistentRepository)
{
    public async Task<ResultBox<bool>> GetEvents(
        EventRetrievalInfo eventRetrievalInfo,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var aggregateContainerGroup = eventRetrievalInfo.GetAggregateContainerGroup();
        if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
        {
            return await temporaryRepository.GetEvents(eventRetrievalInfo, resultAction);
        }
        return await persistentRepository.GetEvents(eventRetrievalInfo, resultAction);
    }
}
