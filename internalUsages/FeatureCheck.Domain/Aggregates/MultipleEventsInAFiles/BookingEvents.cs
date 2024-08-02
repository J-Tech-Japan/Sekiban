using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.MultipleEventsInAFiles;

public static class BookingEvents
{
    public record RoomBooked(
        BookingValueObjects.RoomNumber RoomNumber,
        BookingValueObjects.GuestName GuestName,
        BookingValueObjects.DateOnly CheckInDate,
        BookingValueObjects.DateOnly CheckOutDate,
        BookingValueObjects.Money TotalAmount) : IEventPayload<Booking, RoomBooked>
    {
        public static Booking OnEvent(Booking aggregatePayload, Event<RoomBooked> ev) =>
            aggregatePayload with
            {
                RoomNumber = ev.Payload.RoomNumber,
                GuestName = ev.Payload.GuestName,
                CheckInDate = ev.Payload.CheckInDate,
                CheckOutDate = ev.Payload.CheckOutDate,
                TotalAmount = ev.Payload.TotalAmount
            };
    }

    public record BookingPaid(BookingValueObjects.Money Amount, string PaymentNote)
        : IEventPayload<Booking, BookingPaid>
    {
        public static Booking OnEvent(Booking aggregatePayload, Event<BookingPaid> ev) =>
            aggregatePayload with
            {
                TotalReceived = aggregatePayload.TotalReceived.AddIfPossible(ev.Payload.Amount)
            };
    }
}
