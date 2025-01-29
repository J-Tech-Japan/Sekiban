using ResultBoxes;

namespace Sekiban.Pure.Command.Handlers;

public interface ICommandWithHandlerSerializable: ICommandGetProjector,ICommand
{
    public Delegate GetHandler();
    public Delegate GetPartitionKeysSpecifier();
    public OptionalValue<Type> GetAggregatePayloadType();
}