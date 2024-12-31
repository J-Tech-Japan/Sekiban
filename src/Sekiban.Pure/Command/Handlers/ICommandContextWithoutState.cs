using ResultBoxes;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
namespace Sekiban.Pure.Command.Handlers;

public interface ICommandContextWithoutState
{
    public List<IEvent> Events { get; }
    public PartitionKeys GetPartitionKeys();
    public int GetNextVersion();
    public int GetCurrentVersion();
    internal CommandExecuted GetCommandExecuted(List<IEvent> producedEvents);
    public ResultBox<EventOrNone> AppendEvent(IEventPayload eventPayload);
}
