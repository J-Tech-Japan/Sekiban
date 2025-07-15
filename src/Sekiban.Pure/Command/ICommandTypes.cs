using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Command;

public interface ICommandTypes
{
    public List<Type> GetCommandTypes();
    
    /// <summary>
    /// Gets the type from the command type name.
    /// </summary>
    /// <param name="commandTypeName">Name of the command type</param>
    /// <returns>The found type, or null if not found</returns>
    public Type? GetCommandTypeByName(string commandTypeName);
    
    public Task<ResultBox<CommandResponse>> ExecuteGeneral(
        CommandExecutor executor,
        ICommandWithHandlerSerializable command,
        PartitionKeys partitionKeys,
        CommandMetadata commandMetadata,
        Func<PartitionKeys, IAggregateProjector, Task<ResultBox<Aggregate>>> loader,
        Func<string, List<IEvent>, Task<ResultBox<List<IEvent>>>> saver);
}
