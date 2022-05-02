namespace Sekiban.EventSourcing.Partitions;

/// <summary>
///     JSONから復帰した時はこれがinitされるが、使ってはいけない
/// </summary>
public class CanNotUsePartitionKeyFactory : IPartitionKeyFactory
{
    public string GetPartitionKey(DocumentType documentType) =>
        throw new NotImplementedException();
}
