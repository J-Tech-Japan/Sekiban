using BookBorrowing.Domain.Aggregates.Borrowers.Events;
using BookBorrowing.Domain.ValueObjects;
using ResultBoxes;
using Sekiban.Core.Command;
namespace BookBorrowing.Domain.Aggregates.Borrowers.Commands;

public record ChangeBorrowerName(Guid BorrowerId, Name ChangedName, string Reason)
    : ICommandWithHandler<Borrower, ChangeBorrowerName>
{
    public Guid GetAggregateId() => BorrowerId;
    public static ResultBox<UnitValue> HandleCommand(
        ChangeBorrowerName command,
        ICommandContext<Borrower> context) => context.AppendEvent(
        new BorrowerNameUpdated(command.ChangedName, command.Reason));
}
