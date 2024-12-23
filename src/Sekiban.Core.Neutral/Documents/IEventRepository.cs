using ResultBoxes;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
namespace Sekiban.Core.Documents;

public interface IEventRepository
{
    Task<ResultBox<bool>> GetEvents(EventRetrievalInfo eventRetrievalInfo, Action<IEnumerable<IEvent>> resultAction);
}
