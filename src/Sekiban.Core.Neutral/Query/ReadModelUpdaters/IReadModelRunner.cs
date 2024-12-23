using ResultBoxes;
namespace Sekiban.Core.Query.ReadModelUpdaters;

public interface IReadModelRunner
{
    public ResultBox<IEnumerable<TReadModel>> StartReadModelInstance<TReadModel>(
        ReadModelInstance<TReadModel> readModelInstance) where TReadModel : IReadModelCommon;
}
