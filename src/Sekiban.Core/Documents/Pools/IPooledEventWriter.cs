namespace Sekiban.Core.Documents.Pools;

public interface IPooledEventWriter : IEventWriter
{
    public int GetPoolOrderIndex();
}
