using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Command.Handlers;

public interface ICommandGetProjector
{
    public IAggregateProjector GetProjector();
}
