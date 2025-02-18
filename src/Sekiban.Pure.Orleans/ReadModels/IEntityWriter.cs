namespace Sekiban.Pure.Orleans.ReadModels;

public interface IEntityWriter<TEntity> where TEntity : IReadModelEntity
{
    Task<TEntity?> GetEntityByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId);
    Task<List<TEntity>> GetHistoryEntityByIdAsync(string rootPartitionKey, string aggregateGroup, Guid targetId, string beforeSortableUniqueId);
    Task<TEntity> AddOrUpdateEntityAsync(TEntity entity);
}
