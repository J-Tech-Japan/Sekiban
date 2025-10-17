namespace Sekiban.Infrastructure.IndexedDb.Documents;

public class IndexedDbDocumentRepositoryOptions
{
    public const int DefaultEventChunkSize = 1000;

    private int _eventChunkSize = DefaultEventChunkSize;

    public int EventChunkSize
    {
        get => _eventChunkSize;
        set => _eventChunkSize = value > 0 ? value : DefaultEventChunkSize;
    }
}
