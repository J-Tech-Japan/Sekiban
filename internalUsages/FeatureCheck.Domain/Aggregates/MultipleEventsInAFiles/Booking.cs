using Sekiban.Core.Aggregate;
namespace FeatureCheck.Domain.Aggregates.MultipleEventsInAFiles;

public record Booking(
    BookingValueObjects.RoomNumber RoomNumber,
    BookingValueObjects.GuestName GuestName,
    BookingValueObjects.DateOnly CheckInDate,
    BookingValueObjects.DateOnly CheckOutDate,
    BookingValueObjects.Money TotalAmount,
    BookingValueObjects.Money TotalReceived,
    DateTime? Arrived = null,
    DateTime? Left = null) : IAggregatePayload<Booking>
{
    public static Booking CreateInitialPayload(Booking? _) =>
        new(
            new BookingValueObjects.RoomNumber(default),
            new BookingValueObjects.GuestName(string.Empty, string.Empty),
            new BookingValueObjects.DateOnly(default),
            new BookingValueObjects.DateOnly(default),
            new BookingValueObjects.Money(default, new BookingValueObjects.Currency(string.Empty)),
            BookingValueObjects.Money.ZeroMoney);

    public bool IsFullyPaid() => TotalReceived.IsEqualOrGreaterThan(TotalAmount);

    public bool IsOverPaid() => TotalReceived.IsGreaterThan(TotalAmount);
}
