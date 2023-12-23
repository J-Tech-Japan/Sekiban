using LanguageExt;
using LanguageExt.Common;
namespace Sekiban.Core.Query.ReadModelUpdaters;

public class ReadModelCacheMerger(IReadModelCache readModelCache) : IReadModelCacheMerger
{
    public Result<Unit> PersistWithCache<TReadModel, TId>(ReadModelChanges<TReadModel, TId> changes) where TReadModel : IReadModel<TId> =>
        throw new NotImplementedException();
    public Result<IReadModelRecords<TReadModel, TId>> ReadUnsafeModel<TReadModel, TId>() where TReadModel : IReadModel<TId> =>
        throw new NotImplementedException();
}