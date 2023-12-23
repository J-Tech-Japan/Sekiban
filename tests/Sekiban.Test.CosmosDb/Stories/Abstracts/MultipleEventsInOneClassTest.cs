using FeatureCheck.Domain.Aggregates.MultipleEventsInAFiles;
using FeatureCheck.Domain.Shared;
using Sekiban.Testing.Story;
using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
namespace Sekiban.Test.CosmosDb.Stories.Abstracts;

public abstract class MultipleEventsInOneClassTest : TestBase<FeatureCheckDependency>
{
    public MultipleEventsInOneClassTest(
        SekibanTestFixture sekibanTestFixture,
        ITestOutputHelper output,
        ISekibanServiceProviderGenerator providerGenerator) : base(sekibanTestFixture, output, providerGenerator)
    {
    }

    [Fact]
    public async Task Test()
    {
        var commandResult = await commandExecutor.ExecCommandAsync(
            new BookingCommands.BookRoom(
                new BookingValueObjects.RoomNumber(123),
                new BookingValueObjects.GuestName("Test", "User"),
                BookingValueObjects.DateOnly.CreateFromDateTime(new DateTime(2023, 8, 8)),
                BookingValueObjects.DateOnly.CreateFromDateTime(new DateTime(2023, 8, 9)),
                BookingValueObjects.Money.USDMoney(120)));
        Assert.NotNull(commandResult.AggregateId);
        var bookingId = commandResult.AggregateId!.Value;

        ResetInMemoryDocumentStoreAndCache();
        var booking = await aggregateLoader.AsDefaultStateAsync<Booking>(bookingId);
        Assert.Equal(
            booking?.Payload,
            new Booking(
                new BookingValueObjects.RoomNumber(123),
                new BookingValueObjects.GuestName("Test", "User"),
                BookingValueObjects.DateOnly.CreateFromDateTime(new DateTime(2023, 8, 8)),
                BookingValueObjects.DateOnly.CreateFromDateTime(new DateTime(2023, 8, 9)),
                BookingValueObjects.Money.USDMoney(120),
                BookingValueObjects.Money.ZeroMoney));

        await commandExecutor.ExecCommandAsync(new BookingCommands.PayBookedRoom(bookingId, BookingValueObjects.Money.USDMoney(120), "Paid in full"));
        ResetInMemoryDocumentStoreAndCache();
        booking = await aggregateLoader.AsDefaultStateAsync<Booking>(bookingId);
        Assert.Equal(
            booking?.Payload,
            new Booking(
                new BookingValueObjects.RoomNumber(123),
                new BookingValueObjects.GuestName("Test", "User"),
                BookingValueObjects.DateOnly.CreateFromDateTime(new DateTime(2023, 8, 8)),
                BookingValueObjects.DateOnly.CreateFromDateTime(new DateTime(2023, 8, 9)),
                BookingValueObjects.Money.USDMoney(120),
                BookingValueObjects.Money.USDMoney(120)));
        Assert.True(booking != null && booking.Payload.IsFullyPaid());
    }
}
