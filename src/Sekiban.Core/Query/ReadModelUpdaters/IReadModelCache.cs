using ResultBoxes;
namespace Sekiban.Core.Query.ReadModelUpdaters;

public interface IReadModelCache
{
    public ResultBox<UnitValue> SetUnsafeReadModels<TReadModel, TId>(UnsafeReadModels<TReadModel, TId> unsafeReadModels)
        where TReadModel : IReadModel<TId>;
    public ResultBox<UnsafeReadModels<TReadModel, TId>> GetUnsafeReadModels<TReadModel, TId>(string serverId, Guid ReadModelInstanceId)
        where TReadModel : IReadModel<TId>;
}
