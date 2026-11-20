using Dcb.EventSource;
using Dcb.EventSource.MeetingRoom.Queries;
using Dcb.EventSource.MeetingRoom.Reservation;
using Dcb.EventSource.MeetingRoom.Room;
using Dcb.EventSource.MeetingRoom.User;
using NUnit.Framework;
using Sekiban.Dcb;
using Sekiban.Dcb.Testing;
using SekibanDcbDeciderAws.ApiService.Auth;

namespace SekibanDcbDeciderAws.Unit;

public class AdministratorReconciliationTests
{
    [Test]
    public void Plan_GrantsMissingAndRevokesDemotedAdministrators()
    {
        var kept = Guid.CreateVersion7();
        var promoted = Guid.CreateVersion7();
        var demoted = Guid.CreateVersion7();

        var plan = AdministratorReconciliation.Create([kept, promoted], [kept, demoted]);

        Assert.That(plan.Grant, Is.EqualTo(new[] { promoted }));
        Assert.That(plan.Revoke, Is.EqualTo(new[] { demoted }));
    }

    [Test]
    public void Plan_IsEmptyWhenBothSourcesAgree()
    {
        var admin = Guid.CreateVersion7();
        var plan = AdministratorReconciliation.Create([admin], [admin]);
        Assert.That(plan.Grant, Is.Empty);
        Assert.That(plan.Revoke, Is.Empty);
    }

    [Test]
    public async Task RevokedDomainAdmin_LosesTheOrganizerOverride_AndRevokeIsIdempotent()
    {
        var domain = DomainType.GetDomainTypes();
        ISekibanExecutor executor = new InMemoryDcbExecutorForTesting(domain, new InMemoryEventStore(domain.EventTypes));
        var organizerId = Guid.CreateVersion7();
        var formerAdminId = Guid.CreateVersion7();
        var roomId = Guid.CreateVersion7();
        foreach (var (id, email) in new[] { (organizerId, "organizer@example.com"), (formerAdminId, "former@example.com") })
        {
            await executor.ExecuteAsync(new RegisterUser { UserId = id, DisplayName = email, Email = email });
            await executor.ExecuteAsync(new GrantUserAccess { UserId = id, InitialRole = "User" });
        }
        await executor.ExecuteAsync(new GrantUserRole { UserId = formerAdminId, Role = "Admin" });
        await executor.ExecuteAsync(new CreateRoom
        {
            RoomId = roomId, Name = "Room", Location = "Floor 1", Capacity = 4, RequiresApproval = false
        });

        // The reconciliation demotes the user (twice: the second run is a no-op)
        await executor.ExecuteAsync(new RevokeUserRole { UserId = formerAdminId, Role = AdministratorReconciliation.AdminRole });
        var second = await executor.ExecuteAsync(new RevokeUserRole { UserId = formerAdminId, Role = AdministratorReconciliation.AdminRole });
        Assert.That(second.Events, Is.Empty);

        var admins = await executor.QueryAsync(new GetUserAccessListQuery
        {
            ActiveOnly = true, RoleFilter = AdministratorReconciliation.AdminRole,
            WaitForSortableUniqueId = null
        });
        Assert.That(admins.Items.Select(i => i.UserId), Does.Not.Contain(formerAdminId));

        var reservationId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1, 9, 0, 0, DateTimeKind.Utc).AddMonths(1).AddDays(2);
        await executor.ExecuteAsync(new CreateQuickReservation
        {
            ReservationId = reservationId, RoomId = roomId, OrganizerId = organizerId, OrganizerName = "Organizer",
            StartTime = start, EndTime = start.AddHours(1), Purpose = "Sync"
        });
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => executor.ExecuteAsync(new CancelReservation
        {
            ReservationId = reservationId, Reason = "x", RequestedByUserId = formerAdminId
        }));
    }
}
