using Sekiban.Pure.Documents;
namespace Sekiban.Pure.Command.Handlers;

public interface ICommandPartitionSpecifier<TCommand> where TCommand : ICommand, IEquatable<TCommand>
{
    public PartitionKeys SpecifyPartitionKeys(TCommand command);
}
