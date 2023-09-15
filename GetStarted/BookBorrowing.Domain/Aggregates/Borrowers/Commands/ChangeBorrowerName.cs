using BookBorrowing.Domain.Aggregates.Borrowers.Events;
using BookBorrowing.Domain.ValueObjects;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace BookBorrowing.Domain.Aggregates.Borrowers.Commands;

public record ChangeBorrowerName(Guid BorrowerId, Name ChangedName, string Reason) : ICommand<Borrower>
{
    public Guid GetAggregateId() => BorrowerId;
    public class Handler : ICommandHandler<Borrower, ChangeBorrowerName>
    {
        public async IAsyncEnumerable<IEventPayloadApplicableTo<Borrower>> HandleCommandAsync(
            Func<AggregateState<Borrower>> getAggregateState,
            ChangeBorrowerName command)
        {
            await Task.CompletedTask;
            yield return new BorrowerNameUpdated(command.ChangedName, command.Reason);
        }
    }
}
