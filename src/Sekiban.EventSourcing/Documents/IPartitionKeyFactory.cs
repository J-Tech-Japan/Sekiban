namespace Sekiban.EventSourcing.Documents
{
    public interface IPartitionKeyFactory
    {
        string GetPartitionKey(DocumentType documentType);
    }
}
