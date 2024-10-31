namespace Sekiban.Core.Documents.Pools;

public interface IPooledEventRepository : IEventRepository
{
    public int GetPoolOrderIndex();
}
