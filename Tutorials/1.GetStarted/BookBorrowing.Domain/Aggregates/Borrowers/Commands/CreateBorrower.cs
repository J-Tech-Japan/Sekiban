using BookBorrowing.Domain.Aggregates.Borrowers.Events;
using BookBorrowing.Domain.ValueObjects;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace BookBorrowing.Domain.Aggregates.Borrowers.Commands;

public record CreateBorrower(
    BorrowerCardNo BorrowerCardNo,
    Name Name,
    PhoneNumber PhoneNumber,
    Email Email) : ICommand<Borrower>
{
    public Guid GetAggregateId() => Guid.NewGuid();
    public class Handler : ICommandHandler<Borrower, CreateBorrower>
    {
        public IEnumerable<IEventPayloadApplicableTo<Borrower>> HandleCommand(
            Func<AggregateState<Borrower>> getAggregateState,
            CreateBorrower command)
        {
            yield return new BorrowerCreated(
                command.BorrowerCardNo,
                command.Name,
                command.PhoneNumber,
                command.Email);
        }
    }
}
