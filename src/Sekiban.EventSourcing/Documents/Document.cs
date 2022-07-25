namespace Sekiban.EventSourcing.Documents;

public record Document : IDocument
{

    public Document(
        Guid id,
        string partitionKey,
        DocumentType documentType,
        string documentTypeName,
        DateTime timeStamp,
        string sortableUniqueId)
    {
        Id = id;
        PartitionKey = partitionKey;
        DocumentType = documentType;
        DocumentTypeName = documentTypeName;
        TimeStamp = timeStamp;
        SortableUniqueId = sortableUniqueId;
    }
    public Guid Id
    {
        get;
        init;
    }
    public string PartitionKey
    {
        get;
        init;
    }
    public DocumentType DocumentType
    {
        get;
        init;
    }
    public string DocumentTypeName
    {
        get;
        init;
    }
    public DateTime TimeStamp
    {
        get;
        init;
    }
    public string SortableUniqueId
    {
        get;
        init;
    }
}
