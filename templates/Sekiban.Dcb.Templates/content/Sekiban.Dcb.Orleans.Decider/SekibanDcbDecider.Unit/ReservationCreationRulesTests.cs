using Dcb.EventSource;
using Dcb.EventSource.MeetingRoom.Reservation;
using Dcb.EventSource.MeetingRoom.Room;
using Dcb.EventSource.MeetingRoom.User;
using Dcb.MeetingRoomModels.States.UserMonthlyReservation;
using Dcb.MeetingRoomModels.Tags;
using NUnit.Framework;
using Sekiban.Dcb;
using Sekiban.Dcb.Testing;

namespace SekibanDcbDecider.Unit;

public class ReservationCreationRulesTests
{
    private ISekibanExecutor _executor = null!;
    private Guid _roomId;
    private Guid _userId;

    [SetUp]
    public async Task SetUp()
    {
        var domain = DomainType.GetDomainTypes();
        _executor = new InMemoryDcbExecutorForTesting(domain, new InMemoryEventStore(domain.EventTypes));
        _roomId = Guid.CreateVersion7();
        _userId = Guid.CreateVersion7();
        await _executor.ExecuteAsync(new CreateRoom
        {
            RoomId = _roomId, Name = "Test room", Location = "Test floor", Capacity = 4, RequiresApproval = false
        });
        await _executor.ExecuteAsync(new RegisterUser
        {
            UserId = _userId, DisplayName = "Test user", Email = "user@example.com", MonthlyReservationLimit = 1
        });
        await _executor.ExecuteAsync(new GrantUserAccess { UserId = _userId, InitialRole = "User" });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OrdinaryUserAtLimit_IsRejected(bool quick)
    {
        var start = NextMonth();
        await CreateAsync(quick, start);
        var error = Assert.ThrowsAsync<ApplicationException>(() => CreateAsync(quick, start.AddHours(2)));
        Assert.That(error!.Message, Is.EqualTo("Monthly reservation limit exceeded (1)."));
        Assert.That(await CountAsync(start), Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Administrator_IsExemptFromWindowAndLimit(bool quick)
    {
        await _executor.ExecuteAsync(new GrantUserRole { UserId = _userId, Role = "Admin" });
        var start = NextMonth().AddMonths(1);
        await CreateAsync(quick, start);
        await CreateAsync(quick, start.AddHours(2));
        Assert.That(await CountAsync(start), Is.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OrdinaryUserOutsideWindow_IsRejected(bool quick)
    {
        var error = Assert.ThrowsAsync<ApplicationException>(() => CreateAsync(quick, NextMonth().AddMonths(1)));
        Assert.That(error!.Message, Is.EqualTo("Reservations can only be made for this month or next month."));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task QuickReservation_IsCountedAgainstEitherPath(bool nextQuick)
    {
        var start = NextMonth();
        await CreateAsync(true, start);
        Assert.That(await CountAsync(start), Is.EqualTo(1));
        var error = Assert.ThrowsAsync<ApplicationException>(() => CreateAsync(nextQuick, start.AddHours(2)));
        Assert.That(error!.Message, Is.EqualTo("Monthly reservation limit exceeded (1)."));
    }

    private static DateTime NextMonth()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, 1, 12, 0, 0, DateTimeKind.Utc).AddMonths(1);
    }

    private async Task<int> CountAsync(DateTime start)
    {
        var state = await _executor.GetTagStateAsync<UserMonthlyReservationProjector>(
            UserMonthlyReservationTag.FromStartTime(_userId, start));
        return ((UserMonthlyReservationState)state.Payload).ActiveRequestCount;
    }

    private async Task CreateAsync(bool quick, DateTime start)
    {
        if (quick)
        {
            await _executor.ExecuteAsync(new CreateQuickReservation
            {
                ReservationId = Guid.CreateVersion7(), RoomId = _roomId, OrganizerId = _userId,
                StartTime = start, EndTime = start.AddHours(1), Purpose = "Test reservation"
            });
        }
        else
        {
            await _executor.ExecuteAsync(new CreateReservationDraft
            {
                ReservationId = Guid.CreateVersion7(), RoomId = _roomId, OrganizerId = _userId,
                StartTime = start, EndTime = start.AddHours(1), Purpose = "Test reservation"
            });
        }
    }
}
