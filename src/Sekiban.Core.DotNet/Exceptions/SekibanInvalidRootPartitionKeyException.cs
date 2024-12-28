namespace Sekiban.Core.Exceptions;

/// <summary>
///     This exception is thrown when the root partition key is invalid.
/// </summary>
public class SekibanInvalidRootPartitionKeyException : Exception, ISekibanException
{
    public SekibanInvalidRootPartitionKeyException(string rootPartitionKey) : base(
        $"Invalid root partition key: {rootPartitionKey}")
    {
    }
}
