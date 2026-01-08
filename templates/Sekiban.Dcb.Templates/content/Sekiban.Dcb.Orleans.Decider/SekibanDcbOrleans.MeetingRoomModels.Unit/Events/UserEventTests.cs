using Dcb.MeetingRoomModels.Events.UserAccess;
using Dcb.MeetingRoomModels.Events.UserDirectory;
using Dcb.MeetingRoomModels.Tags;

namespace SekibanDcbOrleans.MeetingRoomModels.Unit.Events;

public class UserEventTests
{
    #region UserDirectory Events

    [Fact]
    public void UserRegistered_Should_Create_With_Properties()
    {
        var userId = Guid.NewGuid();
        var registeredAt = DateTime.UtcNow;
        var ev = new UserRegistered(userId, "John Doe", "john@example.com", "Engineering", registeredAt);

        Assert.Equal(userId, ev.UserId);
        Assert.Equal("John Doe", ev.DisplayName);
        Assert.Equal("john@example.com", ev.Email);
        Assert.Equal("Engineering", ev.Department);
        Assert.Equal(registeredAt, ev.RegisteredAt);
    }

    [Fact]
    public void UserRegistered_GetEventWithTags_Should_Return_UserTag()
    {
        var userId = Guid.NewGuid();
        var ev = new UserRegistered(userId, "John Doe", "john@example.com", null, DateTime.UtcNow);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Equal(ev, eventWithTags.Event);
        Assert.Single(eventWithTags.Tags);
        Assert.Contains(eventWithTags.Tags, t => t is UserTag tag && tag.UserId == userId);
    }

    [Fact]
    public void UserProfileUpdated_Should_Create_With_Properties()
    {
        var userId = Guid.NewGuid();
        var ev = new UserProfileUpdated(userId, "Jane Doe", "jane@example.com", "Sales");

        Assert.Equal(userId, ev.UserId);
        Assert.Equal("Jane Doe", ev.DisplayName);
        Assert.Equal("jane@example.com", ev.Email);
        Assert.Equal("Sales", ev.Department);
    }

    [Fact]
    public void UserProfileUpdated_GetEventWithTags_Should_Return_UserTag()
    {
        var userId = Guid.NewGuid();
        var ev = new UserProfileUpdated(userId, "Jane Doe", "jane@example.com", "Sales");

        var eventWithTags = ev.GetEventWithTags();

        Assert.Single(eventWithTags.Tags);
        Assert.Contains(eventWithTags.Tags, t => t is UserTag tag && tag.UserId == userId);
    }

    [Fact]
    public void UserDeactivated_Should_Create_With_Properties()
    {
        var userId = Guid.NewGuid();
        var deactivatedAt = DateTime.UtcNow;
        var ev = new UserDeactivated(userId, "Left company", deactivatedAt);

        Assert.Equal(userId, ev.UserId);
        Assert.Equal("Left company", ev.Reason);
        Assert.Equal(deactivatedAt, ev.DeactivatedAt);
    }

    [Fact]
    public void UserDeactivated_GetEventWithTags_Should_Return_UserTag()
    {
        var userId = Guid.NewGuid();
        var ev = new UserDeactivated(userId, null, DateTime.UtcNow);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Single(eventWithTags.Tags);
        Assert.Contains(eventWithTags.Tags, t => t is UserTag tag && tag.UserId == userId);
    }

    [Fact]
    public void UserReactivated_Should_Create_With_Properties()
    {
        var userId = Guid.NewGuid();
        var reactivatedAt = DateTime.UtcNow;
        var ev = new UserReactivated(userId, reactivatedAt);

        Assert.Equal(userId, ev.UserId);
        Assert.Equal(reactivatedAt, ev.ReactivatedAt);
    }

    [Fact]
    public void UserReactivated_GetEventWithTags_Should_Return_UserTag()
    {
        var userId = Guid.NewGuid();
        var ev = new UserReactivated(userId, DateTime.UtcNow);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Single(eventWithTags.Tags);
        Assert.Contains(eventWithTags.Tags, t => t is UserTag tag && tag.UserId == userId);
    }

    #endregion

    #region UserAccess Events

    [Fact]
    public void UserAccessGranted_Should_Create_With_Properties()
    {
        var userId = Guid.NewGuid();
        var grantedAt = DateTime.UtcNow;
        var ev = new UserAccessGranted(userId, "User", grantedAt);

        Assert.Equal(userId, ev.UserId);
        Assert.Equal("User", ev.InitialRole);
        Assert.Equal(grantedAt, ev.GrantedAt);
    }

    [Fact]
    public void UserAccessGranted_GetEventWithTags_Should_Return_UserAccessTag()
    {
        var userId = Guid.NewGuid();
        var ev = new UserAccessGranted(userId, "User", DateTime.UtcNow);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Single(eventWithTags.Tags);
        Assert.Contains(eventWithTags.Tags, t => t is UserAccessTag tag && tag.UserId == userId);
    }

    [Fact]
    public void UserRoleGranted_Should_Create_With_Properties()
    {
        var userId = Guid.NewGuid();
        var grantedAt = DateTime.UtcNow;
        var ev = new UserRoleGranted(userId, "Admin", grantedAt);

        Assert.Equal(userId, ev.UserId);
        Assert.Equal("Admin", ev.Role);
        Assert.Equal(grantedAt, ev.GrantedAt);
    }

    [Fact]
    public void UserRoleGranted_GetEventWithTags_Should_Return_UserAccessTag()
    {
        var userId = Guid.NewGuid();
        var ev = new UserRoleGranted(userId, "Admin", DateTime.UtcNow);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Single(eventWithTags.Tags);
        Assert.Contains(eventWithTags.Tags, t => t is UserAccessTag tag && tag.UserId == userId);
    }

    [Fact]
    public void UserRoleRevoked_Should_Create_With_Properties()
    {
        var userId = Guid.NewGuid();
        var revokedAt = DateTime.UtcNow;
        var ev = new UserRoleRevoked(userId, "Admin", revokedAt);

        Assert.Equal(userId, ev.UserId);
        Assert.Equal("Admin", ev.Role);
        Assert.Equal(revokedAt, ev.RevokedAt);
    }

    [Fact]
    public void UserRoleRevoked_GetEventWithTags_Should_Return_UserAccessTag()
    {
        var userId = Guid.NewGuid();
        var ev = new UserRoleRevoked(userId, "Admin", DateTime.UtcNow);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Single(eventWithTags.Tags);
        Assert.Contains(eventWithTags.Tags, t => t is UserAccessTag tag && tag.UserId == userId);
    }

    [Fact]
    public void UserAccessDeactivated_Should_Create_With_Properties()
    {
        var userId = Guid.NewGuid();
        var deactivatedAt = DateTime.UtcNow;
        var ev = new UserAccessDeactivated(userId, "Security violation", deactivatedAt);

        Assert.Equal(userId, ev.UserId);
        Assert.Equal("Security violation", ev.Reason);
        Assert.Equal(deactivatedAt, ev.DeactivatedAt);
    }

    [Fact]
    public void UserAccessDeactivated_GetEventWithTags_Should_Return_UserAccessTag()
    {
        var userId = Guid.NewGuid();
        var ev = new UserAccessDeactivated(userId, null, DateTime.UtcNow);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Single(eventWithTags.Tags);
        Assert.Contains(eventWithTags.Tags, t => t is UserAccessTag tag && tag.UserId == userId);
    }

    [Fact]
    public void UserAccessReactivated_Should_Create_With_Properties()
    {
        var userId = Guid.NewGuid();
        var reactivatedAt = DateTime.UtcNow;
        var ev = new UserAccessReactivated(userId, reactivatedAt);

        Assert.Equal(userId, ev.UserId);
        Assert.Equal(reactivatedAt, ev.ReactivatedAt);
    }

    [Fact]
    public void UserAccessReactivated_GetEventWithTags_Should_Return_UserAccessTag()
    {
        var userId = Guid.NewGuid();
        var ev = new UserAccessReactivated(userId, DateTime.UtcNow);

        var eventWithTags = ev.GetEventWithTags();

        Assert.Single(eventWithTags.Tags);
        Assert.Contains(eventWithTags.Tags, t => t is UserAccessTag tag && tag.UserId == userId);
    }

    #endregion
}
