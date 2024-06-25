using BookBorrowing.Domain.Aggregates.Borrowers.Events;
using BookBorrowing.Domain.ValueObjects;
using ResultBoxes;
using Sekiban.Core.Command;
namespace BookBorrowing.Domain.Aggregates.Borrowers.Commands;

public record CreateBorrower(
    BorrowerCardNo BorrowerCardNo,
    Name Name,
    PhoneNumber PhoneNumber,
    Email Email) : ICommandWithHandler<Borrower, CreateBorrower>
{
    public Guid GetAggregateId() => Guid.NewGuid();
    public static ResultBox<UnitValue> HandleCommand(
        CreateBorrower command,
        ICommandContext<Borrower> context) => context.AppendEvent(
        new BorrowerCreated(
            command.BorrowerCardNo,
            command.Name,
            command.PhoneNumber,
            command.Email));
}
