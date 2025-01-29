using ResultBoxes;

namespace Sekiban.Pure.Projectors;

public interface IAggregateProjectorSpecifier
{
    ResultBox<IAggregateProjector> GetProjector(string projectorName);
}