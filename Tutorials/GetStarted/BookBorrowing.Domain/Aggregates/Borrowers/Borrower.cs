using BookBorrowing.Domain.ValueObjects;
using Sekiban.Core.Aggregate;
namespace BookBorrowing.Domain.Aggregates.Borrowers;

public record Borrower(
    BorrowerCardNo BorrowerCardNo,
    Name Name,
    PhoneNumber PhoneNumber,
    Email Email,
    BorrowerStatus BorrowerStatus = BorrowerStatus.Active) : IAggregatePayload<Borrower>
{
    public static Borrower CreateInitialPayload(Borrower? _) => new(
        new BorrowerCardNo(0),
        new Name(string.Empty, string.Empty),
        new PhoneNumber(string.Empty),
        new Email(string.Empty));
}
