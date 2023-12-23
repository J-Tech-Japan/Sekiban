using FeatureCheck.Domain.Aggregates.MultipleEventsInAFiles;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing.SingleProjections;
using System;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class BookingSpec : AggregateTest<Booking, FeatureCheckDependency>
{
    [Fact]
    public void CreateBookingSpec()
    {
        // When
        WhenCommand(
            new BookingCommands.BookRoom(
                new BookingValueObjects.RoomNumber(123),
                new BookingValueObjects.GuestName("John", "Doe"),
                new BookingValueObjects.DateOnly(new DateTime(2021, 1, 1)),
                new BookingValueObjects.DateOnly(new DateTime(2021, 1, 2)),
                BookingValueObjects.Money.JPYMoney(1000)));

        // Then
        ThenPayloadIs(
            new Booking(
                new BookingValueObjects.RoomNumber(123),
                new BookingValueObjects.GuestName("John", "Doe"),
                new BookingValueObjects.DateOnly(new DateTime(2021, 1, 1)),
                new BookingValueObjects.DateOnly(new DateTime(2021, 1, 2)),
                BookingValueObjects.Money.JPYMoney(1000),
                BookingValueObjects.Money.ZeroMoney));
        ThenGetPayload(payload => Assert.False(payload.IsFullyPaid()));
    }
    [Fact]
    public void CreateAndPayBookingSpec()
    {
        // Given
        GivenCommand(
            new BookingCommands.BookRoom(
                new BookingValueObjects.RoomNumber(123),
                new BookingValueObjects.GuestName("John", "Doe"),
                new BookingValueObjects.DateOnly(new DateTime(2021, 1, 1)),
                new BookingValueObjects.DateOnly(new DateTime(2021, 1, 2)),
                BookingValueObjects.Money.JPYMoney(1000)));
        // When
        WhenCommand(new BookingCommands.PayBookedRoom(GetAggregateId(), BookingValueObjects.Money.JPYMoney(1000), "Paid in full"));

        // Then
        ThenPayloadIs(
            new Booking(
                new BookingValueObjects.RoomNumber(123),
                new BookingValueObjects.GuestName("John", "Doe"),
                new BookingValueObjects.DateOnly(new DateTime(2021, 1, 1)),
                new BookingValueObjects.DateOnly(new DateTime(2021, 1, 2)),
                BookingValueObjects.Money.JPYMoney(1000),
                BookingValueObjects.Money.JPYMoney(1000)));
        ThenGetPayload(payload => Assert.True(payload.IsFullyPaid()));
    }

    [Fact]
    public void CreateAndPayButNotFullyBookingSpec()
    {
        // Given
        GivenCommand(
            new BookingCommands.BookRoom(
                new BookingValueObjects.RoomNumber(123),
                new BookingValueObjects.GuestName("John", "Doe"),
                new BookingValueObjects.DateOnly(new DateTime(2021, 1, 1)),
                new BookingValueObjects.DateOnly(new DateTime(2021, 1, 2)),
                BookingValueObjects.Money.JPYMoney(1000)));
        // When
        WhenCommand(new BookingCommands.PayBookedRoom(GetAggregateId(), BookingValueObjects.Money.JPYMoney(999), "Paid in full"));
        // Then
        ThenPayloadIs(
            new Booking(
                new BookingValueObjects.RoomNumber(123),
                new BookingValueObjects.GuestName("John", "Doe"),
                new BookingValueObjects.DateOnly(new DateTime(2021, 1, 1)),
                new BookingValueObjects.DateOnly(new DateTime(2021, 1, 2)),
                BookingValueObjects.Money.JPYMoney(1000),
                BookingValueObjects.Money.JPYMoney(999)));
        ThenGetPayload(payload => Assert.False(payload.IsFullyPaid()));
    }

    [Fact]
    public void CreateAndPayInTwiceBookingSpec()
    {
        // Given
        GivenCommand(
            new BookingCommands.BookRoom(
                new BookingValueObjects.RoomNumber(123),
                new BookingValueObjects.GuestName("John", "Doe"),
                new BookingValueObjects.DateOnly(new DateTime(2021, 1, 1)),
                new BookingValueObjects.DateOnly(new DateTime(2021, 1, 2)),
                BookingValueObjects.Money.JPYMoney(1000)));
        GivenCommand(new BookingCommands.PayBookedRoom(GetAggregateId(), BookingValueObjects.Money.JPYMoney(300), "Paid first"));
        // When        
        WhenCommand(new BookingCommands.PayBookedRoom(GetAggregateId(), BookingValueObjects.Money.JPYMoney(700), "Paid in full"));
        // Then
        ThenPayloadIs(
            new Booking(
                new BookingValueObjects.RoomNumber(123),
                new BookingValueObjects.GuestName("John", "Doe"),
                new BookingValueObjects.DateOnly(new DateTime(2021, 1, 1)),
                new BookingValueObjects.DateOnly(new DateTime(2021, 1, 2)),
                BookingValueObjects.Money.JPYMoney(1000),
                BookingValueObjects.Money.JPYMoney(1000)));
        ThenGetPayload(payload => Assert.True(payload.IsFullyPaid()));
    }
}
