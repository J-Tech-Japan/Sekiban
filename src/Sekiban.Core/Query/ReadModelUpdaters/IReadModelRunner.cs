using DotNext;
namespace Sekiban.Core.Query.ReadModelUpdaters;

public interface IReadModelRunner
{
    public Result<IEnumerable<TReadModel>> StartReadModelInstance<TReadModel>(ReadModelInstance<TReadModel> readModelInstance)
        where TReadModel : IReadModelCommon;
}
