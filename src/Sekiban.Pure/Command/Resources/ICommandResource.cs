using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Command.Handlers;

public interface ICommandResource<TCommand> where TCommand : ICommand, IEquatable<TCommand>
{
    public Func<TCommand, PartitionKeys> GetSpecifyPartitionKeysFunc();
    public OptionalValue<Type> GetAggregatePayloadType();
    public Type GetCommandType();
    public IAggregateProjector GetProjector();
    public object? GetInjection();
    public Delegate GetHandler();
}
