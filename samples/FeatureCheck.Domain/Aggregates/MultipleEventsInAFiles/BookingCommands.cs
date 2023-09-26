using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
namespace FeatureCheck.Domain.Aggregates.MultipleEventsInAFiles;

public static class BookingCommands
{
    public record BookRoom(
        BookingValueObjects.RoomNumber RoomNumber,
        BookingValueObjects.GuestName GuestName,
        BookingValueObjects.DateOnly CheckInDate,
        BookingValueObjects.DateOnly CheckOutDate,
        BookingValueObjects.Money TotalAmount) : ICommand<Booking>
    {
        public Guid GetAggregateId() => Guid.NewGuid();

        public class Handler : ICommandHandler<Booking, BookRoom>
        {
            public IEnumerable<IEventPayloadApplicableTo<Booking>> HandleCommand(Func<AggregateState<Booking>> getAggregateState, BookRoom command)
            {
                yield return new BookingEvents.RoomBooked(
                    command.RoomNumber,
                    command.GuestName,
                    command.CheckInDate,
                    command.CheckOutDate,
                    command.TotalAmount);
            }
        }
    }
    public record PayBookedRoom(Guid BookingId, BookingValueObjects.Money Payment, string Note) : ICommand<Booking>
    {
        public Guid GetAggregateId() => BookingId;
        public class Handler : ICommandHandler<Booking, PayBookedRoom>
        {

            public IEnumerable<IEventPayloadApplicableTo<Booking>> HandleCommand(
                Func<AggregateState<Booking>> getAggregateState,
                PayBookedRoom command)
            {
                if (!getAggregateState().Payload.IsFullyPaid() && getAggregateState().Payload.TotalAmount.CanAdd(command.Payment))
                {
                    yield return new BookingEvents.BookingPaid(command.Payment, command.Note);
                }
            }
        }
    }
}
