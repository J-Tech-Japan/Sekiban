using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ICreateAggregateCommandHandler<T, C> : IAggregateCommandHandler where T : IAggregatePayload, new() where C : ICreateAggregateCommand<T>
{
    public Task<AggregateCommandResponse> HandleAsync(AggregateCommandDocument<C> aggregateCommandDocument, Aggregate<T> aggregateState);
    public Guid GenerateAggregateId(C command);
    C CleanupCommandIfNeeded(C command);
}
