using BookBorrowing.Domain.Aggregates.Borrowers.Events;
using BookBorrowing.Domain.ValueObjects;
using ResultBoxes;
using Sekiban.Core.Command;
namespace BookBorrowing.Domain.Aggregates.Borrowers.Commands;

public record CreateBorrower(BorrowerCardNo BorrowerCardNo, Name Name, PhoneNumber PhoneNumber, Email Email)
    : ICommandWithHandler<Borrower, CreateBorrower>
{
    public static Guid SpecifyAggregateId(CreateBorrower command) => Guid.NewGuid();

    public static ResultBox<EventOrNone<Borrower>> HandleCommand(
        CreateBorrower command,
        ICommandContext<Borrower> context) =>
        EventOrNone.Event(
            new BorrowerCreated(command.BorrowerCardNo, command.Name, command.PhoneNumber, command.Email));
}
