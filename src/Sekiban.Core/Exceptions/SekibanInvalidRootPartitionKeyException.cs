namespace Sekiban.Core.Exceptions;

public class SekibanInvalidRootPartitionKeyException : Exception, ISekibanException
{
    public SekibanInvalidRootPartitionKeyException(string rootPartitionKey) : base($"Invalid root partition key: {rootPartitionKey}")
    {
    }
}
