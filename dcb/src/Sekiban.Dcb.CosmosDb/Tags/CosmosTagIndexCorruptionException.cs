namespace Sekiban.Dcb.CosmosDb.Tags;

/// <summary>
///     Raised when a tag row already exists at the identity a write derives for an (event, tag) pair, but
///     its content differs from what that pair derives. Because a tag row is a pure projection of the event
///     document (see <see cref="CosmosTagIdentity" />), this can only mean the tags container disagrees with
///     the events container — the index is corrupt, or something outside Sekiban wrote to it.
///     This exception is NOT retryable. Re-executing the write will derive exactly the same content and hit
///     exactly the same mismatch, so retry/backoff logic will spin without making progress. Consumers with
///     broad catch-and-retry policies should treat it as terminal, surface it, and repair the index rather
///     than retry. The write never silently overwrites the existing row.
/// </summary>
public class CosmosTagIndexCorruptionException : Exception
{
    /// <summary>
    ///     Creates a corruption exception.
    /// </summary>
    public CosmosTagIndexCorruptionException()
    {
    }

    /// <summary>
    ///     Creates a corruption exception with a message.
    /// </summary>
    public CosmosTagIndexCorruptionException(string message) : base(message)
    {
    }

    /// <summary>
    ///     Creates a corruption exception with a message and inner exception.
    /// </summary>
    public CosmosTagIndexCorruptionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    ///     Creates a corruption exception describing the conflicting tag row.
    /// </summary>
    public CosmosTagIndexCorruptionException(
        string serviceId,
        string tag,
        string partitionKey,
        string documentId,
        string expectedContent,
        string actualContent)
        : base(
            $"Tag index corruption detected for service '{serviceId}', tag '{tag}'. " +
            $"An existing tag row at pk='{partitionKey}', id='{documentId}' does not match the row derived " +
            $"from the event. The existing row was left untouched. This is not retryable — the same content " +
            $"is derived on every attempt. Expected: [{expectedContent}]. Actual: [{actualContent}].")
    {
        ServiceId = serviceId;
        Tag = tag;
        PartitionKey = partitionKey;
        DocumentId = documentId;
        ExpectedContent = expectedContent;
        ActualContent = actualContent;
    }

    /// <summary>
    ///     Service id the write was scoped to.
    /// </summary>
    public string? ServiceId { get; }

    /// <summary>
    ///     Tag whose row conflicts.
    /// </summary>
    public string? Tag { get; }

    /// <summary>
    ///     Partition key of the conflicting tag row.
    /// </summary>
    public string? PartitionKey { get; }

    /// <summary>
    ///     Document id of the conflicting tag row.
    /// </summary>
    public string? DocumentId { get; }

    /// <summary>
    ///     Content derived from the event for this (event, tag) pair.
    /// </summary>
    public string? ExpectedContent { get; }

    /// <summary>
    ///     Content of the tag row currently stored.
    /// </summary>
    public string? ActualContent { get; }
}
