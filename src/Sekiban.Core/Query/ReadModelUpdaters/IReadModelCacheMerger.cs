using ResultBoxes;
namespace Sekiban.Core.Query.ReadModelUpdaters;

public interface IReadModelCacheMerger
{
    public ResultBox<UnitValue> PersistWithCache<TReadModel, TId>(ReadModelChanges<TReadModel, TId> changes)
        where TReadModel : IReadModel<TId>;
    public ResultBox<IReadModelRecords<TReadModel, TId>> ReadUnsafeModel<TReadModel, TId>()
        where TReadModel : IReadModel<TId>;
}
