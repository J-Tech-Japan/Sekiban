namespace Sekiban.Dcb.Sqlite;

/// <summary>
///     Metadata about the cache state, used for synchronization
/// </summary>
public record CacheMetadata
{
    /// <summary>
    ///     Remote endpoint identifier (e.g., Cosmos DB endpoint URL)
    /// </summary>
    public string RemoteEndpoint { get; init; } = "";

    /// <summary>
    ///     Database name on the remote endpoint
    /// </summary>
    public string DatabaseName { get; init; } = "";

    /// <summary>
    ///     Schema version for cache invalidation
    /// </summary>
    public string SchemaVersion { get; init; } = "1.0";

    /// <summary>
    ///     Total event count at last fetch
    /// </summary>
    public long TotalCountAtFetch { get; init; }

    /// <summary>
    ///     Last cached SortableUniqueId
    /// </summary>
    public string? LastCachedSortableUniqueId { get; init; }

    /// <summary>
    ///     Last safe window threshold UTC
    /// </summary>
    public DateTime? LastSafeWindowUtc { get; init; }

    /// <summary>
    ///     When this cache was created
    /// </summary>
    public DateTime CreatedUtc { get; init; }

    /// <summary>
    ///     When this cache was last updated
    /// </summary>
    public DateTime UpdatedUtc { get; init; }
}
