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
    /// コマンド型名から型を取得します。
    /// </summary>
    /// <param name="commandTypeName">コマンド型の名前</param>
    /// <returns>見つかった型、または見つからない場合はnull</returns>
    public Type? GetCommandTypeByName(string commandTypeName);
    
    public Task<ResultBox<CommandResponse>> ExecuteGeneral(
        CommandExecutor executor,
        ICommandWithHandlerSerializable command,
        PartitionKeys partitionKeys,
        CommandMetadata commandMetadata,
        Func<PartitionKeys, IAggregateProjector, Task<ResultBox<Aggregate>>> loader,
        Func<string, List<IEvent>, Task<ResultBox<List<IEvent>>>> saver);
}
