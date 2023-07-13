namespace Sekiban.Core.Command;

/// <summary>
///     Await command execution if same instance is running command which has same Aggregate Type and Aggregate Id.
///     It awaits until other thread finished execution.
///     Note : This only awaits within same instance.
///     Using multi server or multi instance, this does not work.
///     This interface is used for internal. Application developer does not need to implement this interface.
/// </summary>
public interface ICommandExecuteAwaiter
{
    /// <summary>
    ///     Start task for the aggregate.
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    void StartTask<TAggregatePayload>(Guid aggregateId);
    /// <summary>
    ///     Wait until other thread finished execution.
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    Task WaitUntilOtherThreadFinished<TAggregatePayload>(Guid aggregateId);
    /// <summary>
    ///     End task for the aggregate.
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    void EndTask<TAggregatePayload>(Guid aggregateId);
}
