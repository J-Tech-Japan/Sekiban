namespace Sekiban.Core.Documents;

public interface IPartitionKeyFactory
{
    string GetPartitionKey(DocumentType documentType);
}
