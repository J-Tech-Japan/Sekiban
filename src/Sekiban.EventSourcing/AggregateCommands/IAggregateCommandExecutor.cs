namespace Sekiban.EventSourcing.AggregateCommands;

public interface IAggregateCommandExecutor
{
    Task<AggregateCommandExecutorResponse<Q, C>> ExecChangeCommandAsync<T, Q, C>(C command, List<CallHistory>? callHistories = null)
        where T : TransferableAggregateBase<Q> where Q : AggregateDtoBase, new() where C : ChangeAggregateCommandBase<T>;
    Task<AggregateCommandExecutorResponse<Q, C>> ExecCreateCommandAsync<T, Q, C>(C command, List<CallHistory>? callHistories = null)
        where T : TransferableAggregateBase<Q> where Q : AggregateDtoBase, new() where C : ICreateAggregateCommand<T>;
}
