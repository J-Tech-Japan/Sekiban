using Dcb.MeetingRoomModels.Events.UserDirectory;
using Dcb.MeetingRoomModels.States.UserDirectory;
using Dcb.MeetingRoomModels.States.UserDirectory.Deciders;

namespace SekibanDcbOrleans.MeetingRoomModels.Unit.States;

public class UserDirectoryDeciderTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly DateTime _registeredAt = DateTime.UtcNow;

    #region UserRegistered Tests

    [Fact]
    public void UserDirectoryState_Empty_Should_Be_UserDirectoryEmpty()
    {
        var state = UserDirectoryState.Empty;

        Assert.IsType<UserDirectoryState.UserDirectoryEmpty>(state);
    }

    [Fact]
    public void UserRegisteredDecider_Create_Should_Return_Active_State()
    {
        var ev = new UserRegistered(_userId, "John Doe", "john@example.com", "Engineering", _registeredAt);

        var state = UserRegisteredDecider.Create(ev);

        Assert.Equal(_userId, state.UserId);
        Assert.Equal("John Doe", state.DisplayName);
        Assert.Equal("john@example.com", state.Email);
        Assert.Equal("Engineering", state.Department);
        Assert.Equal(_registeredAt, state.RegisteredAt);
    }

    [Fact]
    public void UserRegisteredDecider_Evolve_From_Empty_Should_Return_Active_State()
    {
        var state = UserDirectoryState.Empty;
        var ev = new UserRegistered(_userId, "John Doe", "john@example.com", "Engineering", _registeredAt);

        var newState = state.Evolve(ev);

        var active = Assert.IsType<UserDirectoryState.UserDirectoryActive>(newState);
        Assert.Equal(_userId, active.UserId);
    }

    [Fact]
    public void UserRegisteredDecider_Validate_Empty_Should_Not_Throw()
    {
        var state = UserDirectoryState.Empty;

        var exception = Record.Exception(() => UserRegisteredDecider.Validate(state));

        Assert.Null(exception);
    }

    [Fact]
    public void UserRegisteredDecider_Validate_Already_Registered_Should_Throw()
    {
        var ev = new UserRegistered(_userId, "John Doe", "john@example.com", "Engineering", _registeredAt);
        var state = UserRegisteredDecider.Create(ev);

        Assert.Throws<InvalidOperationException>(() => UserRegisteredDecider.Validate(state));
    }

    #endregion

    #region UserProfileUpdated Tests

    [Fact]
    public void UserProfileUpdatedDecider_Evolve_Should_Update_Profile()
    {
        var registerEvent = new UserRegistered(_userId, "John Doe", "john@example.com", "Engineering", _registeredAt);
        var state = UserDirectoryState.Empty.Evolve(registerEvent);

        var updateEvent = new UserProfileUpdated(_userId, "John Smith", "john.smith@example.com", "Sales");
        var newState = state.Evolve(updateEvent);

        var active = Assert.IsType<UserDirectoryState.UserDirectoryActive>(newState);
        Assert.Equal("John Smith", active.DisplayName);
        Assert.Equal("john.smith@example.com", active.Email);
        Assert.Equal("Sales", active.Department);
        Assert.Equal(_registeredAt, active.RegisteredAt); // Original registration date preserved
    }

    [Fact]
    public void UserProfileUpdatedDecider_Validate_Active_Should_Not_Throw()
    {
        var ev = new UserRegistered(_userId, "John Doe", "john@example.com", "Engineering", _registeredAt);
        var state = UserRegisteredDecider.Create(ev);

        var exception = Record.Exception(() => UserProfileUpdatedDecider.Validate(state));

        Assert.Null(exception);
    }

    [Fact]
    public void UserProfileUpdatedDecider_Validate_Empty_Should_Throw()
    {
        var state = UserDirectoryState.Empty;

        Assert.Throws<InvalidOperationException>(() => UserProfileUpdatedDecider.Validate(state));
    }

    #endregion

    #region UserDeactivated Tests

    [Fact]
    public void UserDeactivatedDecider_Evolve_Should_Return_Deactivated_State()
    {
        var registerEvent = new UserRegistered(_userId, "John Doe", "john@example.com", "Engineering", _registeredAt);
        var state = UserDirectoryState.Empty.Evolve(registerEvent);

        var deactivatedAt = DateTime.UtcNow;
        var deactivateEvent = new UserDeactivated(_userId, "Left company", deactivatedAt);
        var newState = state.Evolve(deactivateEvent);

        var deactivated = Assert.IsType<UserDirectoryState.UserDirectoryDeactivated>(newState);
        Assert.Equal(_userId, deactivated.UserId);
        Assert.Equal("John Doe", deactivated.DisplayName);
        Assert.Equal("Left company", deactivated.DeactivationReason);
        Assert.Equal(deactivatedAt, deactivated.DeactivatedAt);
    }

    [Fact]
    public void UserDeactivatedDecider_Validate_Active_Should_Not_Throw()
    {
        var ev = new UserRegistered(_userId, "John Doe", "john@example.com", "Engineering", _registeredAt);
        var state = UserRegisteredDecider.Create(ev);

        var exception = Record.Exception(() => UserDeactivatedDecider.Validate(state));

        Assert.Null(exception);
    }

    [Fact]
    public void UserDeactivatedDecider_Validate_Empty_Should_Throw()
    {
        var state = UserDirectoryState.Empty;

        Assert.Throws<InvalidOperationException>(() => UserDeactivatedDecider.Validate(state));
    }

    #endregion

    #region UserReactivated Tests

    [Fact]
    public void UserReactivatedDecider_Evolve_Should_Return_Active_State()
    {
        // Register -> Deactivate -> Reactivate
        var registerEvent = new UserRegistered(_userId, "John Doe", "john@example.com", "Engineering", _registeredAt);
        var deactivateEvent = new UserDeactivated(_userId, "Left company", DateTime.UtcNow);
        var reactivateEvent = new UserReactivated(_userId, DateTime.UtcNow);

        var state = UserDirectoryState.Empty
            .Evolve(registerEvent)
            .Evolve(deactivateEvent)
            .Evolve(reactivateEvent);

        var active = Assert.IsType<UserDirectoryState.UserDirectoryActive>(state);
        Assert.Equal(_userId, active.UserId);
        Assert.Equal("John Doe", active.DisplayName);
    }

    [Fact]
    public void UserReactivatedDecider_Validate_Deactivated_Should_Not_Throw()
    {
        var registerEvent = new UserRegistered(_userId, "John Doe", "john@example.com", "Engineering", _registeredAt);
        var deactivateEvent = new UserDeactivated(_userId, "Left company", DateTime.UtcNow);

        UserDirectoryState state = UserDirectoryState.Empty
            .Evolve(registerEvent)
            .Evolve(deactivateEvent);

        var exception = Record.Exception(() => UserReactivatedDecider.Validate(state));

        Assert.Null(exception);
    }

    [Fact]
    public void UserReactivatedDecider_Validate_Active_Should_Throw()
    {
        var ev = new UserRegistered(_userId, "John Doe", "john@example.com", "Engineering", _registeredAt);
        var state = UserRegisteredDecider.Create(ev);

        Assert.Throws<InvalidOperationException>(() => UserReactivatedDecider.Validate(state));
    }

    #endregion

    #region State Transition Scenarios

    [Fact]
    public void UserDirectory_Full_Lifecycle_Should_Work()
    {
        var state = UserDirectoryState.Empty;

        // Register
        var registerEvent = new UserRegistered(_userId, "John Doe", "john@example.com", "Engineering", _registeredAt);
        state = state.Evolve(registerEvent);
        Assert.IsType<UserDirectoryState.UserDirectoryActive>(state);

        // Update profile
        var updateEvent = new UserProfileUpdated(_userId, "John Smith", "john.smith@example.com", "Sales");
        state = state.Evolve(updateEvent);
        var active = Assert.IsType<UserDirectoryState.UserDirectoryActive>(state);
        Assert.Equal("John Smith", active.DisplayName);

        // Deactivate
        var deactivateEvent = new UserDeactivated(_userId, "Temporary leave", DateTime.UtcNow);
        state = state.Evolve(deactivateEvent);
        Assert.IsType<UserDirectoryState.UserDirectoryDeactivated>(state);

        // Reactivate
        var reactivateEvent = new UserReactivated(_userId, DateTime.UtcNow);
        state = state.Evolve(reactivateEvent);
        active = Assert.IsType<UserDirectoryState.UserDirectoryActive>(state);
        Assert.Equal("John Smith", active.DisplayName); // Profile preserved after reactivation
    }

    #endregion
}
