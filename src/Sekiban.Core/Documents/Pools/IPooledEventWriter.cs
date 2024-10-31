using ResultBoxes;
using Sekiban.Core.Events;
namespace Sekiban.Core.Documents.Pools;

public interface IPooledDocumentWriter : IDocumentWriter
{
    public int GetPoolOrderIndex();
}
public interface IPooledEventRepository : IEventRepository
{
    public int GetPoolOrderIndex();
}
public class DirectPoolRepository : IPooledEventRepository
{

    public Task<ResultBox<bool>> GetEvents(
        EventRetrievalInfo eventRetrievalInfo,
        Action<IEnumerable<IEvent>> resultAction) => throw new NotImplementedException();
    public int GetPoolOrderIndex() => throw new NotImplementedException();
}
