using BookBorrowing.Domain.Aggregates.Borrowers.Events;
using BookBorrowing.Domain.ValueObjects;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace BookBorrowing.Domain.Aggregates.Borrowers.Commands;

public record ChangeBorrowerName(Guid BorrowerId, Name ChangedName, string Reason) : ICommand<Borrower>
{
    public Guid GetAggregateId() => BorrowerId;
    public class Handler : ICommandHandler<Borrower, ChangeBorrowerName>
    {
        public IEnumerable<IEventPayloadApplicableTo<Borrower>> HandleCommand(ChangeBorrowerName command, ICommandContext<Borrower> context)
        { 
            yield return new BorrowerNameUpdated(command.ChangedName, command.Reason);
        }
    }
}
