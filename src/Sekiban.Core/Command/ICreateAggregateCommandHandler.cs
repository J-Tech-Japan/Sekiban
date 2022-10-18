using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Command;

public interface ICreateAggregateCommandHandler<T, C> : IAggregateCommandHandler where T : IAggregate where C : ICreateAggregateCommand<T>
{
    public Task<AggregateCommandResponse> HandleAsync(AggregateCommandDocument<C> aggregateCommandDocument, T aggregate);
    public Guid GenerateAggregateId(C command);
    C CleanupCommandIfNeeded(C command);
}
