using BookBorrowing.Domain.ValueObjects;
using Sekiban.Core.Events;
namespace BookBorrowing.Domain.Aggregates.Borrowers.Events;

public record BorrowerCreated(
    BorrowerCardNo BorrowerCardNo,
    Name Name,
    PhoneNumber PhoneNumber,
    Email Email) : IEventPayload<Borrower, BorrowerCreated>
{
    public static Borrower OnEvent(Borrower aggregatePayload, Event<BorrowerCreated> ev) =>
        aggregatePayload with
        {
            BorrowerCardNo = ev.Payload.BorrowerCardNo,
            Name = ev.Payload.Name,
            PhoneNumber = ev.Payload.PhoneNumber,
            Email = ev.Payload.Email
        };
}
