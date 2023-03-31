namespace Sekiban.Core.Command;

public interface ICommandExecuteAwaiter
{
    void StartTask<TAggregatePayload>(Guid aggregateId);
    Task WaitUntilOtherThreadFinished<TAggregatePayload>(Guid aggregateId);
    void EndTask<TAggregatePayload>(Guid aggregateId);
}
