using ResultBoxes;
using Sekiban.Pure.Events;

namespace Sekiban.Pure.Projectors;

public interface IMultiProjectorsType
{
    ResultBox<IMultiProjectorCommon> Project(IMultiProjectorCommon multiProjector, IEvent ev);

    ResultBox<IMultiProjectorCommon> Project(IMultiProjectorCommon multiProjector, IReadOnlyList<IEvent> events)
    {
        return ResultBox.FromValue(events.ToList())
            .ReduceEach(multiProjector, (ev, common) => Project(common, ev));
    }

    IMultiProjectorCommon GetProjectorFromGrainName(string grainName);
    IMultiProjectorStateCommon ToTypedState(MultiProjectionState state);
}