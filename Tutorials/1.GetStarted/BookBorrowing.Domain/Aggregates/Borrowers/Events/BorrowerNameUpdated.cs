using BookBorrowing.Domain.ValueObjects;
using Sekiban.Core.Events;
namespace BookBorrowing.Domain.Aggregates.Borrowers.Events;

public record BorrowerNameUpdated(Name Name, string Reason) : IEventPayload<Borrower, BorrowerNameUpdated>
{
    public static Borrower OnEvent(Borrower aggregatePayload, Event<BorrowerNameUpdated> ev) => aggregatePayload with { Name = ev.Payload.Name };
}
