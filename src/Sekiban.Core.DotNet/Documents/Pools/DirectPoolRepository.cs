using ResultBoxes;
using Sekiban.Core.Events;
namespace Sekiban.Core.Documents.Pools;

public class DirectPoolRepository : IPooledEventRepository
{
    public Task<ResultBox<bool>> GetEvents(
        EventRetrievalInfo eventRetrievalInfo,
        Action<IEnumerable<IEvent>> resultAction) => throw new NotImplementedException();
    public int GetPoolOrderIndex() => throw new NotImplementedException();
}
