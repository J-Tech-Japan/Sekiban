using Dapr.Actors;
using ResultBoxes;
using Sekiban.Pure.Query;

namespace Sekiban.Pure.Dapr.Actors;

public interface IMultiProjectorActor : IActor
{
    Task<ResultBox<object>> QueryAsync(IQueryCommon query);
}