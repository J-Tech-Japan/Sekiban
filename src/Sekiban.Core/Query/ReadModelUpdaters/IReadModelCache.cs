using LanguageExt;
using LanguageExt.Common;
namespace Sekiban.Core.Query.ReadModelUpdaters;

public interface IReadModelCache
{
    public Result<Unit> SetUnsafeReadModels<TReadModel, TId>(UnsafeReadModels<TReadModel, TId> unsafeReadModels) where TReadModel : IReadModel<TId>;
    public Result<UnsafeReadModels<TReadModel, TId>> GetUnsafeReadModels<TReadModel, TId>(string serverId, Guid ReadModelInstanceId)
        where TReadModel : IReadModel<TId>;
}
