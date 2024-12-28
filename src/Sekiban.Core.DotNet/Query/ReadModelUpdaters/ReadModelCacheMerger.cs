using ResultBoxes;
namespace Sekiban.Core.Query.ReadModelUpdaters;

public class ReadModelCacheMerger : IReadModelCacheMerger
{
    public ResultBox<UnitValue> PersistWithCache<TReadModel, TId>(ReadModelChanges<TReadModel, TId> changes)
        where TReadModel : IReadModel<TId> =>
        throw new NotImplementedException();
    public ResultBox<IReadModelRecords<TReadModel, TId>> ReadUnsafeModel<TReadModel, TId>()
        where TReadModel : IReadModel<TId> =>
        throw new NotImplementedException();
}
