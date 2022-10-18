namespace Sekiban.Core.Document;

public interface IPartitionKeyFactory
{
    string GetPartitionKey(DocumentType documentType);
}
