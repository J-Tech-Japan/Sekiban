using Dcb.MeetingRoomModels.Events.UserAccess;
using Dcb.MeetingRoomModels.States.UserAccess;
using Dcb.MeetingRoomModels.States.UserAccess.Deciders;

namespace SekibanDcbOrleans.MeetingRoomModels.Unit.States;

public class UserAccessDeciderTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly DateTime _grantedAt = DateTime.UtcNow;

    #region UserAccessGranted Tests

    [Fact]
    public void UserAccessState_Empty_Should_Be_UserAccessEmpty()
    {
        var state = UserAccessState.Empty;

        Assert.IsType<UserAccessState.UserAccessEmpty>(state);
    }

    [Fact]
    public void UserAccessGrantedDecider_Create_Should_Return_Active_State_With_Initial_Role()
    {
        var ev = new UserAccessGranted(_userId, "User", _grantedAt);

        var state = UserAccessGrantedDecider.Create(ev);

        Assert.Equal(_userId, state.UserId);
        Assert.Single(state.Roles);
        Assert.Contains("User", state.Roles);
        Assert.Equal(_grantedAt, state.GrantedAt);
    }

    [Fact]
    public void UserAccessGrantedDecider_Evolve_From_Empty_Should_Return_Active_State()
    {
        var state = UserAccessState.Empty;
        var ev = new UserAccessGranted(_userId, "User", _grantedAt);

        var newState = state.Evolve(ev);

        var active = Assert.IsType<UserAccessState.UserAccessActive>(newState);
        Assert.Equal(_userId, active.UserId);
        Assert.Contains("User", active.Roles);
    }

    [Fact]
    public void UserAccessGrantedDecider_Validate_Empty_Should_Not_Throw()
    {
        var state = UserAccessState.Empty;

        var exception = Record.Exception(() => UserAccessGrantedDecider.Validate(state));

        Assert.Null(exception);
    }

    [Fact]
    public void UserAccessGrantedDecider_Validate_Already_Granted_Should_Throw()
    {
        var ev = new UserAccessGranted(_userId, "User", _grantedAt);
        var state = UserAccessGrantedDecider.Create(ev);

        Assert.Throws<InvalidOperationException>(() => UserAccessGrantedDecider.Validate(state));
    }

    #endregion

    #region UserRoleGranted Tests

    [Fact]
    public void UserRoleGrantedDecider_Evolve_Should_Add_Role()
    {
        var grantAccessEvent = new UserAccessGranted(_userId, "User", _grantedAt);
        var state = UserAccessState.Empty.Evolve(grantAccessEvent);

        var grantRoleEvent = new UserRoleGranted(_userId, "Admin", DateTime.UtcNow);
        var newState = state.Evolve(grantRoleEvent);

        var active = Assert.IsType<UserAccessState.UserAccessActive>(newState);
        Assert.Equal(2, active.Roles.Count);
        Assert.Contains("User", active.Roles);
        Assert.Contains("Admin", active.Roles);
    }

    [Fact]
    public void UserRoleGrantedDecider_Evolve_Duplicate_Role_Should_Not_Add()
    {
        var grantAccessEvent = new UserAccessGranted(_userId, "User", _grantedAt);
        var state = UserAccessState.Empty.Evolve(grantAccessEvent);

        var grantRoleEvent = new UserRoleGranted(_userId, "User", DateTime.UtcNow);
        var newState = state.Evolve(grantRoleEvent);

        var active = Assert.IsType<UserAccessState.UserAccessActive>(newState);
        Assert.Single(active.Roles); // Still just one "User" role
    }

    [Fact]
    public void UserRoleGrantedDecider_Validate_Active_Should_Not_Throw()
    {
        var ev = new UserAccessGranted(_userId, "User", _grantedAt);
        var state = UserAccessGrantedDecider.Create(ev);

        var exception = Record.Exception(() => UserRoleGrantedDecider.Validate(state, "Admin"));

        Assert.Null(exception);
    }

    [Fact]
    public void UserRoleGrantedDecider_Validate_Duplicate_Role_Should_Throw()
    {
        var ev = new UserAccessGranted(_userId, "User", _grantedAt);
        var state = UserAccessGrantedDecider.Create(ev);

        Assert.Throws<InvalidOperationException>(() => UserRoleGrantedDecider.Validate(state, "User"));
    }

    [Fact]
    public void UserRoleGrantedDecider_Validate_Empty_Should_Throw()
    {
        var state = UserAccessState.Empty;

        Assert.Throws<InvalidOperationException>(() => UserRoleGrantedDecider.Validate(state, "Admin"));
    }

    #endregion

    #region UserRoleRevoked Tests

    [Fact]
    public void UserRoleRevokedDecider_Evolve_Should_Remove_Role()
    {
        var grantAccessEvent = new UserAccessGranted(_userId, "User", _grantedAt);
        var grantRoleEvent = new UserRoleGranted(_userId, "Admin", DateTime.UtcNow);
        var state = UserAccessState.Empty
            .Evolve(grantAccessEvent)
            .Evolve(grantRoleEvent);

        var revokeEvent = new UserRoleRevoked(_userId, "Admin", DateTime.UtcNow);
        var newState = state.Evolve(revokeEvent);

        var active = Assert.IsType<UserAccessState.UserAccessActive>(newState);
        Assert.Single(active.Roles);
        Assert.Contains("User", active.Roles);
        Assert.DoesNotContain("Admin", active.Roles);
    }

    [Fact]
    public void UserRoleRevokedDecider_Evolve_NonExistent_Role_Should_Not_Change()
    {
        var grantAccessEvent = new UserAccessGranted(_userId, "User", _grantedAt);
        var state = UserAccessState.Empty.Evolve(grantAccessEvent);

        var revokeEvent = new UserRoleRevoked(_userId, "NonExistent", DateTime.UtcNow);
        var newState = state.Evolve(revokeEvent);

        var active = Assert.IsType<UserAccessState.UserAccessActive>(newState);
        Assert.Single(active.Roles);
        Assert.Contains("User", active.Roles);
    }

    [Fact]
    public void UserRoleRevokedDecider_Validate_HasRole_Should_Not_Throw()
    {
        var ev = new UserAccessGranted(_userId, "User", _grantedAt);
        var state = UserAccessGrantedDecider.Create(ev);

        var exception = Record.Exception(() => UserRoleRevokedDecider.Validate(state, "User"));

        Assert.Null(exception);
    }

    [Fact]
    public void UserRoleRevokedDecider_Validate_NoRole_Should_Throw()
    {
        var ev = new UserAccessGranted(_userId, "User", _grantedAt);
        var state = UserAccessGrantedDecider.Create(ev);

        Assert.Throws<InvalidOperationException>(() => UserRoleRevokedDecider.Validate(state, "Admin"));
    }

    #endregion

    #region UserAccessDeactivated Tests

    [Fact]
    public void UserAccessDeactivatedDecider_Evolve_Should_Return_Deactivated_State()
    {
        var grantAccessEvent = new UserAccessGranted(_userId, "User", _grantedAt);
        var state = UserAccessState.Empty.Evolve(grantAccessEvent);

        var deactivatedAt = DateTime.UtcNow;
        var deactivateEvent = new UserAccessDeactivated(_userId, "Security violation", deactivatedAt);
        var newState = state.Evolve(deactivateEvent);

        var deactivated = Assert.IsType<UserAccessState.UserAccessDeactivated>(newState);
        Assert.Equal(_userId, deactivated.UserId);
        Assert.Single(deactivated.Roles);
        Assert.Equal("Security violation", deactivated.DeactivationReason);
        Assert.Equal(deactivatedAt, deactivated.DeactivatedAt);
    }

    [Fact]
    public void UserAccessDeactivatedDecider_Validate_Active_Should_Not_Throw()
    {
        var ev = new UserAccessGranted(_userId, "User", _grantedAt);
        var state = UserAccessGrantedDecider.Create(ev);

        var exception = Record.Exception(() => UserAccessDeactivatedDecider.Validate(state));

        Assert.Null(exception);
    }

    [Fact]
    public void UserAccessDeactivatedDecider_Validate_Empty_Should_Throw()
    {
        var state = UserAccessState.Empty;

        Assert.Throws<InvalidOperationException>(() => UserAccessDeactivatedDecider.Validate(state));
    }

    #endregion

    #region UserAccessReactivated Tests

    [Fact]
    public void UserAccessReactivatedDecider_Evolve_Should_Return_Active_State()
    {
        var grantAccessEvent = new UserAccessGranted(_userId, "User", _grantedAt);
        var grantRoleEvent = new UserRoleGranted(_userId, "Admin", DateTime.UtcNow);
        var deactivateEvent = new UserAccessDeactivated(_userId, "Temporary", DateTime.UtcNow);
        var reactivateEvent = new UserAccessReactivated(_userId, DateTime.UtcNow);

        var state = UserAccessState.Empty
            .Evolve(grantAccessEvent)
            .Evolve(grantRoleEvent)
            .Evolve(deactivateEvent)
            .Evolve(reactivateEvent);

        var active = Assert.IsType<UserAccessState.UserAccessActive>(state);
        Assert.Equal(_userId, active.UserId);
        Assert.Equal(2, active.Roles.Count);
        Assert.Contains("User", active.Roles);
        Assert.Contains("Admin", active.Roles);
    }

    [Fact]
    public void UserAccessReactivatedDecider_Validate_Deactivated_Should_Not_Throw()
    {
        var grantAccessEvent = new UserAccessGranted(_userId, "User", _grantedAt);
        var deactivateEvent = new UserAccessDeactivated(_userId, "Temporary", DateTime.UtcNow);

        UserAccessState state = UserAccessState.Empty
            .Evolve(grantAccessEvent)
            .Evolve(deactivateEvent);

        var exception = Record.Exception(() => UserAccessReactivatedDecider.Validate(state));

        Assert.Null(exception);
    }

    [Fact]
    public void UserAccessReactivatedDecider_Validate_Active_Should_Throw()
    {
        var ev = new UserAccessGranted(_userId, "User", _grantedAt);
        var state = UserAccessGrantedDecider.Create(ev);

        Assert.Throws<InvalidOperationException>(() => UserAccessReactivatedDecider.Validate(state));
    }

    #endregion

    #region State Transition Scenarios

    [Fact]
    public void UserAccess_Full_Lifecycle_Should_Work()
    {
        var state = UserAccessState.Empty;

        // Grant access
        var grantAccessEvent = new UserAccessGranted(_userId, "User", _grantedAt);
        state = state.Evolve(grantAccessEvent);
        var active = Assert.IsType<UserAccessState.UserAccessActive>(state);
        Assert.Single(active.Roles);

        // Add roles
        state = state.Evolve(new UserRoleGranted(_userId, "Admin", DateTime.UtcNow));
        state = state.Evolve(new UserRoleGranted(_userId, "Approver", DateTime.UtcNow));
        active = Assert.IsType<UserAccessState.UserAccessActive>(state);
        Assert.Equal(3, active.Roles.Count);

        // Remove a role
        state = state.Evolve(new UserRoleRevoked(_userId, "Approver", DateTime.UtcNow));
        active = Assert.IsType<UserAccessState.UserAccessActive>(state);
        Assert.Equal(2, active.Roles.Count);
        Assert.DoesNotContain("Approver", active.Roles);

        // Deactivate
        state = state.Evolve(new UserAccessDeactivated(_userId, "Audit pending", DateTime.UtcNow));
        Assert.IsType<UserAccessState.UserAccessDeactivated>(state);

        // Reactivate
        state = state.Evolve(new UserAccessReactivated(_userId, DateTime.UtcNow));
        active = Assert.IsType<UserAccessState.UserAccessActive>(state);
        Assert.Equal(2, active.Roles.Count); // Roles preserved after reactivation
        Assert.Contains("User", active.Roles);
        Assert.Contains("Admin", active.Roles);
    }

    [Fact]
    public void UserAccessActive_HasRole_Should_Return_True_For_Existing_Role()
    {
        var grantAccessEvent = new UserAccessGranted(_userId, "User", _grantedAt);
        var grantRoleEvent = new UserRoleGranted(_userId, "Admin", DateTime.UtcNow);

        var state = UserAccessState.Empty
            .Evolve(grantAccessEvent)
            .Evolve(grantRoleEvent);

        var active = Assert.IsType<UserAccessState.UserAccessActive>(state);
        Assert.True(active.HasRole("User"));
        Assert.True(active.HasRole("Admin"));
        Assert.False(active.HasRole("SuperAdmin"));
    }

    #endregion
}
