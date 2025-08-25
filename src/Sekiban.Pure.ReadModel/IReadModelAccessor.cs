namespace Sekiban.Pure.ReadModel;

/// <summary>
///     Interface for read model entity writers
/// </summary>
public interface IReadModelAccessor<TEntity> where TEntity : IReadModelEntity
{
    /// <summary>
    ///     Get entity by ID
    /// </summary>
    Task<TEntity?> GetEntityByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId);

    /// <summary>
    ///     Get entity history by ID
    /// </summary>
    Task<List<TEntity>> GetHistoryEntityByIdAsync(
        string rootPartitionKey,
        string aggregateGroup,
        Guid targetId,
        string beforeSortableUniqueId);

    /// <summary>
    ///     Add or update entity
    /// </summary>
    Task<TEntity> AddOrUpdateEntityAsync(TEntity entity);

    /// <summary>
    ///     Get last sortable unique ID
    /// </summary>
    Task<string> GetLastSortableUniqueIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId);
}
