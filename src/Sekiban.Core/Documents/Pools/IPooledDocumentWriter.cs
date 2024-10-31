namespace Sekiban.Core.Documents.Pools;

public interface IPooledDocumentWriter : IDocumentWriter
{
    public int GetPoolOrderIndex();
}
