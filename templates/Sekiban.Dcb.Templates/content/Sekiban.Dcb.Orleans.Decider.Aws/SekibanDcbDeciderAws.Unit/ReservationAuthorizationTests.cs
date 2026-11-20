using Dcb.EventSource;
using Dcb.EventSource.MeetingRoom.ApprovalRequest;
using Dcb.EventSource.MeetingRoom.Queries;
using Dcb.EventSource.MeetingRoom.Reservation;
using Dcb.EventSource.MeetingRoom.Room;
using Dcb.EventSource.MeetingRoom.User;
using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Dcb.MeetingRoomModels.States.ApprovalRequest;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.Tags;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Sekiban.Dcb;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Testing;
using SekibanDcbDeciderAws.ApiService.Endpoints;
using SekibanDcbDeciderAws.ApiService.Exceptions;

namespace SekibanDcbDeciderAws.Unit;

/// <summary>
///     Who may manage rooms and reservations, and which reservation transitions are allowed.
/// </summary>
public class ReservationAuthorizationTests
{
    private ISekibanExecutor _executor = null!;
    private Guid _openRoomId;
    private Guid _approvalRoomId;
    private Guid _organizerId;
    private Guid _otherUserId;
    private Guid _adminId;

    [SetUp]
    public async Task SetUp()
    {
        var domain = DomainType.GetDomainTypes();
        _executor = new InMemoryDcbExecutorForTesting(domain, new InMemoryEventStore(domain.EventTypes));
        _openRoomId = Guid.CreateVersion7();
        _approvalRoomId = Guid.CreateVersion7();
        _organizerId = Guid.CreateVersion7();
        _otherUserId = Guid.CreateVersion7();
        _adminId = Guid.CreateVersion7();

        await _executor.ExecuteAsync(new CreateRoom
        {
            RoomId = _openRoomId, Name = "Open room", Location = "Floor 1", Capacity = 4, RequiresApproval = false
        });
        await _executor.ExecuteAsync(new CreateRoom
        {
            RoomId = _approvalRoomId, Name = "Board room", Location = "Floor 2", Capacity = 10, RequiresApproval = true
        });
        await RegisterAsync(_organizerId, "Organizer", "organizer@example.com", "User");
        await RegisterAsync(_otherUserId, "Other", "other@example.com", "User");
        await RegisterAsync(_adminId, "Admin", "admin@example.com", "User");
    }

    // D-1 / D-2: endpoint policies

    [TestCase("POST", "/api/rooms/")]
    [TestCase("PUT", "/api/rooms/{roomId:guid}")]
    [TestCase("POST", "/api/reservations/{reservationId:guid}/reject")]
    [TestCase("POST", "/api/reservations/{reservationId:guid}/confirm")]
    [TestCase("POST", "/api/approvals/{approvalRequestId:guid}/decision")]
    [TestCase("POST", "/api/test-data/generate")]
    [TestCase("POST", "/api/test-data/generate-rooms")]
    [TestCase("POST", "/api/test-data/generate-reservations")]
    public void ManagementEndpoints_RequireAdminPolicy(string method, string route) =>
        Assert.That(PoliciesFor(method, route), Does.Contain("AdminOnly"));

    [TestCase("GET", "/api/rooms/")]
    [TestCase("POST", "/api/reservations/{reservationId:guid}/cancel")]
    [TestCase("POST", "/api/reservations/quick")]
    public void MemberEndpoints_DoNotRequireAdminPolicy(string method, string route) =>
        Assert.That(PoliciesFor(method, route), Does.Not.Contain("AdminOnly"));

    // D-2: cancel is limited to the organizer or an administrator

    [Test]
    public async Task Cancel_ByAnotherUser_IsForbidden()
    {
        var reservationId = await QuickReserveAsync(_openRoomId);
        var error = Assert.ThrowsAsync<UnauthorizedAccessException>(() => CancelAsync(reservationId, _otherUserId));
        Assert.That(error!.Message, Does.Contain("organizer or an administrator"));
        Assert.That(await ReservationStateAsync(reservationId), Is.InstanceOf<ReservationState.ReservationConfirmed>());
    }

    [Test]
    public async Task Cancel_WithoutActingUser_IsForbidden()
    {
        var reservationId = await QuickReserveAsync(_openRoomId);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => CancelAsync(reservationId, Guid.Empty));
    }

    [Test]
    public async Task Cancel_ByOrganizer_Succeeds()
    {
        var reservationId = await QuickReserveAsync(_openRoomId);
        await CancelAsync(reservationId, _organizerId);
        Assert.That(await ReservationStateAsync(reservationId), Is.InstanceOf<ReservationState.ReservationCancelled>());
    }

    [Test]
    public async Task Cancel_WhileAwaitingApproval_WithdrawsTheApprovalRequest()
    {
        var (reservationId, approvalRequestId) = await QuickReserveWithApprovalAsync();
        await CancelAsync(reservationId, _organizerId);

        var withdrawn = await ApprovalStateAsync(approvalRequestId) as ApprovalRequestState.ApprovalRequestWithdrawn;
        Assert.That(withdrawn, Is.Not.Null);
        Assert.That(withdrawn!.Reason, Is.EqualTo("No longer needed"));
        var inboxItem = (await InboxAsync()).Single(i => i.ApprovalRequestId == approvalRequestId);
        Assert.That(inboxItem.Status, Is.EqualTo("Cancelled"));
        var pending = await _executor.QueryAsync(new GetApprovalInboxQuery { PendingOnly = true });
        Assert.That(pending.Items.Select(i => i.ApprovalRequestId), Does.Not.Contain(approvalRequestId));
    }

    [Test]
    public async Task Cancel_ByAdministrator_Succeeds()
    {
        var reservationId = await QuickReserveAsync(_openRoomId);
        await CancelAsync(reservationId, _adminId, isAdmin: true);
        Assert.That(await ReservationStateAsync(reservationId), Is.InstanceOf<ReservationState.ReservationCancelled>());
    }

    [Test]
    public async Task Cancel_ByDomainAdminWithoutAuthenticatedAdminRole_IsForbidden()
    {
        // The domain UserAccess role is not an authorization source; only the token's Admin role is
        await _executor.ExecuteAsync(new GrantUserRole { UserId = _otherUserId, Role = "Admin" });
        var reservationId = await QuickReserveAsync(_openRoomId);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => CancelAsync(reservationId, _otherUserId));
    }

    [Test]
    public async Task Hold_ByDomainAdminWithoutAuthenticatedAdminRole_IsForbidden()
    {
        await _executor.ExecuteAsync(new GrantUserRole { UserId = _otherUserId, Role = "Admin" });
        var reservationId = await CreateDraftAsync(_openRoomId);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => HoldAsync(reservationId, _otherUserId));
    }

    [Test]
    public void DraftFromRequestBody_ClientSuppliedAdminFlagAndOrganizer_AreOverwritten()
    {
        var json = $$"""
            {"reservationId":"{{Guid.CreateVersion7()}}","roomId":"{{_openRoomId}}",
             "organizerId":"{{_adminId}}","organizerName":"Admin","actingUserIsAdmin":true,
             "startTime":"2030-01-01T09:00:00Z","endTime":"2030-01-01T10:00:00Z","purpose":"x"}
            """;
        var fromClient = System.Text.Json.JsonSerializer.Deserialize<CreateReservationDraft>(
            json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.That(fromClient.ActingUserIsAdmin, Is.True); // the body can carry it ...

        var user = new OrganizerContext(_otherUserId, "Other", IsAdmin: false);
        var command = ActingUserCommands.ForActingUser(fromClient, user);

        Assert.That(command.ActingUserIsAdmin, Is.False); // ... but the endpoint overwrites it from the token
        Assert.That(command.OrganizerId, Is.EqualTo(_otherUserId));
        Assert.That(command.OrganizerName, Is.EqualTo("Other"));
    }

    [TestCase("Admin", true)]
    [TestCase("User", false)]
    public void Resolver_TakesAdminFromTheTokenRole(string role, bool expected)
    {
        var context = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                [
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, _otherUserId.ToString()),
                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role)
                ],
                "Test"))
        };
        var result = ReservationOrganizerResolver.Resolve(context);
        Assert.That(result.Organizer!.Value.IsAdmin, Is.EqualTo(expected));
    }

    [Test]
    public async Task TagConsistencyConflict_MapsTo409()
    {
        var context = new DefaultHttpContext
        { RequestServices = new ServiceCollection().BuildServiceProvider(), Response = { Body = new MemoryStream() } };
        var conflict = new InvalidOperationException(
            "Failed to reserve tags: Tag Reservation:x: Tag Reservation:x has been modified. Expected version: a, Current version: b");
        await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance).TryHandleAsync(context, conflict, default);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status409Conflict));
    }

    [Test]
    public async Task OtherInvalidOperation_StaysA500()
    {
        var context = new DefaultHttpContext
        { RequestServices = new ServiceCollection().BuildServiceProvider(), Response = { Body = new MemoryStream() } };
        await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance)
            .TryHandleAsync(context, new InvalidOperationException("Something else"), default);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status500InternalServerError));
    }

    [Test]
    public async Task ForbiddenCancel_MapsTo403()
    {
        var context = new DefaultHttpContext
        { RequestServices = new ServiceCollection().BuildServiceProvider(), Response = { Body = new MemoryStream() } };
        await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance)
            .TryHandleAsync(context, new UnauthorizedAccessException("nope"), default);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status403Forbidden));
    }

    // D-3: reject only applies to a held reservation that is waiting for approval

    [Test]
    public async Task Reject_CancelledReservation_IsRejected()
    {
        var (reservationId, approvalRequestId) = await QuickReserveWithApprovalAsync();
        await CancelAsync(reservationId, _organizerId);

        var error = Assert.ThrowsAsync<ApplicationException>(() => RejectAsync(reservationId, approvalRequestId));
        Assert.That(error!.Message, Does.Contain("not pending"));
        Assert.That(await ApprovalStateAsync(approvalRequestId), Is.InstanceOf<ApprovalRequestState.ApprovalRequestWithdrawn>());
        Assert.That(await ReservationStateAsync(reservationId), Is.InstanceOf<ReservationState.ReservationCancelled>());
    }

    [Test]
    public async Task Reject_ConfirmedReservationWithoutApproval_IsRejected()
    {
        var reservationId = await QuickReserveAsync(_openRoomId);
        Assert.ThrowsAsync<ApplicationException>(() => RejectAsync(reservationId, Guid.CreateVersion7()));
    }

    [Test]
    public async Task Reject_WithUnknownApprovalRequest_IsRejected()
    {
        var (reservationId, _) = await QuickReserveWithApprovalAsync();
        var error = Assert.ThrowsAsync<ApplicationException>(() => RejectAsync(reservationId, Guid.CreateVersion7()));
        Assert.That(error!.Message, Does.Contain("not found"));
    }

    [Test]
    public async Task Decision_WithSecondApprovalRequestOfSameReservation_IsRejected()
    {
        var (reservationId, _) = await QuickReserveWithApprovalAsync();
        var strayApprovalRequestId = Guid.CreateVersion7();
        await _executor.ExecuteAsync(new StartApprovalFlow
        {
            ApprovalRequestId = strayApprovalRequestId, ReservationId = reservationId, RoomId = _approvalRoomId,
            RequesterId = _organizerId
        });

        var error = Assert.ThrowsAsync<ApplicationException>(() =>
            DecideAsync(strayApprovalRequestId, reservationId, ApprovalDecision.Approved));
        Assert.That(error!.Message, Does.Contain("does not belong"));
        Assert.That(await ReservationStateAsync(reservationId), Is.InstanceOf<ReservationState.ReservationHeld>());
        Assert.That(await ApprovalStateAsync(strayApprovalRequestId), Is.InstanceOf<ApprovalRequestState.ApprovalRequestPending>());
    }

    [Test]
    public async Task Decision_OnCancelledReservation_IsRejected_BecauseCancelWithdrewTheRequest()
    {
        var (reservationId, approvalRequestId) = await QuickReserveWithApprovalAsync();
        await CancelAsync(reservationId, _organizerId);

        var error = Assert.ThrowsAsync<ApplicationException>(() =>
            DecideAsync(approvalRequestId, reservationId, ApprovalDecision.Approved));
        Assert.That(error!.Message, Does.Contain("not pending"));
        Assert.That(await ApprovalStateAsync(approvalRequestId), Is.InstanceOf<ApprovalRequestState.ApprovalRequestWithdrawn>());
        Assert.That(await ReservationStateAsync(reservationId), Is.InstanceOf<ReservationState.ReservationCancelled>());
    }

    [Test]
    public async Task ApprovedDecision_ConfirmsReservation_InTheSameCommand()
    {
        var (reservationId, approvalRequestId) = await QuickReserveWithApprovalAsync();
        await DecideAsync(approvalRequestId, reservationId, ApprovalDecision.Approved, "Enjoy");

        Assert.That(await ApprovalStateAsync(approvalRequestId), Is.InstanceOf<ApprovalRequestState.ApprovalRequestApproved>());
        var confirmed = await ReservationStateAsync(reservationId) as ReservationState.ReservationConfirmed;
        Assert.That(confirmed, Is.Not.Null);
        Assert.That(confirmed!.ApprovalDecisionComment, Is.EqualTo("Enjoy"));
    }

    [Test]
    public async Task RejectedDecision_RejectsReservation_InTheSameCommand()
    {
        var (reservationId, approvalRequestId) = await QuickReserveWithApprovalAsync();
        await DecideAsync(approvalRequestId, reservationId, ApprovalDecision.Rejected, "No");

        Assert.That(await ApprovalStateAsync(approvalRequestId), Is.InstanceOf<ApprovalRequestState.ApprovalRequestRejected>());
        Assert.That(await ReservationStateAsync(reservationId), Is.InstanceOf<ReservationState.ReservationRejected>());
    }

    [Test]
    public async Task Reject_HeldReservationAwaitingApproval_Succeeds()
    {
        var (reservationId, approvalRequestId) = await QuickReserveWithApprovalAsync();
        await RejectAsync(reservationId, approvalRequestId);
        Assert.That(await ReservationStateAsync(reservationId), Is.InstanceOf<ReservationState.ReservationRejected>());

        // Rejecting also closes the approval request, so it does not stay pending
        var approval = await ApprovalStateAsync(approvalRequestId) as ApprovalRequestState.ApprovalRequestRejected;
        Assert.That(approval, Is.Not.Null);
        Assert.That(approval!.ApproverId, Is.EqualTo(_adminId));
        Assert.That(approval.Comment, Is.EqualTo("Not available"));
        Assert.That((await InboxAsync()).Where(i => i.Status == "Pending").Select(i => i.ApprovalRequestId),
            Does.Not.Contain(approvalRequestId));
    }

    // D-4: hold uses the draft's room and never trusts an arbitrary approval request id

    [Test]
    public async Task Hold_WithDifferentRoom_IsRejected()
    {
        var reservationId = await CreateDraftAsync(_approvalRoomId);
        var error = Assert.ThrowsAsync<ApplicationException>(() => _executor.ExecuteAsync(new CommitReservationHold
        {
            RequestedByUserId = _organizerId,
            ReservationId = reservationId, RoomId = _openRoomId, RequiresApproval = false
        }));
        Assert.That(error!.Message, Does.Contain("is not for room"));
        Assert.That(await ReservationStateAsync(reservationId), Is.InstanceOf<ReservationState.ReservationDraft>());
    }

    [Test]
    public async Task Hold_WithUnknownApprovalRequest_IsRejected()
    {
        var reservationId = await CreateDraftAsync(_approvalRoomId);
        var error = Assert.ThrowsAsync<ApplicationException>(() => _executor.ExecuteAsync(new CommitReservationHold
        {
            RequestedByUserId = _organizerId,
            ReservationId = reservationId, RoomId = _approvalRoomId, ApprovalRequestId = Guid.CreateVersion7()
        }));
        Assert.That(error!.Message, Does.Contain("not found"));
    }

    [Test]
    public async Task Hold_WithApprovalRequestOfAnotherReservation_IsRejected()
    {
        var (_, otherApprovalRequestId) = await QuickReserveWithApprovalAsync();
        var reservationId = await CreateDraftAsync(_approvalRoomId, NextMonthAt(14));
        var error = Assert.ThrowsAsync<ApplicationException>(() => _executor.ExecuteAsync(new CommitReservationHold
        {
            RequestedByUserId = _organizerId,
            ReservationId = reservationId, RoomId = _approvalRoomId, ApprovalRequestId = otherApprovalRequestId
        }));
        Assert.That(error!.Message, Does.Contain("does not belong"));
    }

    [Test]
    public async Task Hold_ForApprovalRoomWithoutRequest_StartsApprovalFlow()
    {
        var reservationId = await CreateDraftAsync(_approvalRoomId);
        await _executor.ExecuteAsync(new CommitReservationHold
        {
            // RequiresApproval from the client is ignored; the room decides.
            RequestedByUserId = _organizerId,
            ReservationId = reservationId, RequiresApproval = false, ApprovalRequestComment = "Board meeting"
        });

        var held = await ReservationStateAsync(reservationId) as ReservationState.ReservationHeld;
        Assert.That(held, Is.Not.Null);
        Assert.That(held!.RequiresApproval, Is.True);
        Assert.That(held.ApprovalRequestId, Is.Not.Null);

        var approval = await _executor.GetTagStateAsync(
            new TagStateId(new ApprovalRequestTag(held.ApprovalRequestId!.Value), nameof(ApprovalRequestProjector)));
        var pending = approval.Payload as ApprovalRequestState.ApprovalRequestPending;
        Assert.That(pending, Is.Not.Null);
        Assert.That(pending!.ReservationId, Is.EqualTo(reservationId));
        Assert.That(pending.RequestComment, Is.EqualTo("Board meeting"));
    }

    [Test]
    public async Task Hold_WithPendingApprovalOfSameReservation_Succeeds()
    {
        var reservationId = await CreateDraftAsync(_approvalRoomId);
        var approvalRequestId = Guid.CreateVersion7();
        await _executor.ExecuteAsync(new StartApprovalFlow
        {
            ApprovalRequestId = approvalRequestId, ReservationId = reservationId, RoomId = _approvalRoomId,
            RequesterId = _organizerId
        });
        await _executor.ExecuteAsync(new CommitReservationHold
        {
            RequestedByUserId = _organizerId,
            ReservationId = reservationId, ApprovalRequestId = approvalRequestId
        });
        var held = await ReservationStateAsync(reservationId) as ReservationState.ReservationHeld;
        Assert.That(held?.ApprovalRequestId, Is.EqualTo(approvalRequestId));
    }

    // Hold is limited to the draft's organizer or an administrator

    [Test]
    public async Task Hold_ByAnotherUser_IsForbidden()
    {
        var reservationId = await CreateDraftAsync(_openRoomId);
        var error = Assert.ThrowsAsync<UnauthorizedAccessException>(() => HoldAsync(reservationId, _otherUserId));
        Assert.That(error!.Message, Does.Contain("organizer or an administrator"));
        Assert.That(await ReservationStateAsync(reservationId), Is.InstanceOf<ReservationState.ReservationDraft>());
    }

    [Test]
    public async Task Hold_WithoutActingUser_IsForbidden()
    {
        var reservationId = await CreateDraftAsync(_openRoomId);
        Assert.ThrowsAsync<UnauthorizedAccessException>(() => HoldAsync(reservationId, Guid.Empty));
    }

    [Test]
    public async Task Hold_ByOrganizer_Succeeds()
    {
        var reservationId = await CreateDraftAsync(_openRoomId);
        await HoldAsync(reservationId, _organizerId);
        Assert.That(await ReservationStateAsync(reservationId), Is.InstanceOf<ReservationState.ReservationHeld>());
    }

    [Test]
    public async Task Hold_ByAdministrator_Succeeds()
    {
        var reservationId = await CreateDraftAsync(_openRoomId);
        await HoldAsync(reservationId, _adminId, isAdmin: true);
        Assert.That(await ReservationStateAsync(reservationId), Is.InstanceOf<ReservationState.ReservationHeld>());
    }

    [Test]
    public void HoldEndpoint_DoesNotRequireAdminPolicy() =>
        Assert.That(PoliciesFor("POST", "/api/reservations/{reservationId:guid}/hold"), Does.Not.Contain("AdminOnly"));

    // D-5: unknown ids are domain errors (400), not cast failures (500)

    [Test]
    public void Cancel_UnknownReservation_IsDomainError()
    {
        var error = Assert.ThrowsAsync<ApplicationException>(() => CancelAsync(Guid.CreateVersion7(), _adminId, isAdmin: true));
        Assert.That(error!.Message, Does.Contain("not found"));
    }

    [Test]
    public void Confirm_UnknownReservation_IsDomainError()
    {
        var error = Assert.ThrowsAsync<ApplicationException>(() => _executor.ExecuteAsync(new ConfirmReservation
        {
            ReservationId = Guid.CreateVersion7(), RoomId = _openRoomId
        }));
        Assert.That(error!.Message, Does.Contain("not found"));
    }

    [Test]
    public void QuickReservation_ForMissingRoom_IsDomainError()
    {
        var error = Assert.ThrowsAsync<ApplicationException>(() => QuickReserveAsync(Guid.CreateVersion7()));
        Assert.That(error!.Message, Does.Contain("not found"));
    }

    // D-8: the inbox keeps the request time and exposes the decision

    [Test]
    public async Task ApprovalInbox_KeepsRequestedAt_AndShowsDecision()
    {
        var (reservationId, approvalRequestId) = await QuickReserveWithApprovalAsync();
        var pendingItem = (await InboxAsync()).Single(i => i.ApprovalRequestId == approvalRequestId);

        await _executor.ExecuteAsync(new RecordApprovalDecision
        {
            ApprovalRequestId = approvalRequestId, ReservationId = reservationId, ApproverId = _adminId,
            Decision = ApprovalDecision.Rejected, Comment = "Room is closed that day"
        });

        var decided = (await InboxAsync()).Single(i => i.ApprovalRequestId == approvalRequestId);
        Assert.That(decided.Status, Is.EqualTo("Rejected"));
        Assert.That(decided.RequestedAt, Is.EqualTo(pendingItem.RequestedAt));
        Assert.That(decided.DecidedAt, Is.Not.Null);
        Assert.That(decided.DecidedAt, Is.GreaterThanOrEqualTo(decided.RequestedAt));
        Assert.That(decided.DecisionComment, Is.EqualTo("Room is closed that day"));
    }

    private async Task<List<ApprovalInboxItem>> InboxAsync()
    {
        var result = await _executor.QueryAsync(new GetApprovalInboxQuery { PendingOnly = false });
        return result.Items.ToList();
    }

    private async Task RegisterAsync(Guid userId, string name, string email, string role)
    {
        await _executor.ExecuteAsync(new RegisterUser { UserId = userId, DisplayName = name, Email = email });
        await _executor.ExecuteAsync(new GrantUserAccess { UserId = userId, InitialRole = role });
    }

    private async Task<Guid> QuickReserveAsync(Guid roomId)
    {
        var reservationId = Guid.CreateVersion7();
        var start = NextMonthAt(9);
        await _executor.ExecuteAsync(new CreateQuickReservation
        {
            ReservationId = reservationId, RoomId = roomId, OrganizerId = _organizerId, OrganizerName = "Organizer",
            StartTime = start, EndTime = start.AddHours(1), Purpose = "Sync"
        });
        return reservationId;
    }

    private async Task<(Guid ReservationId, Guid ApprovalRequestId)> QuickReserveWithApprovalAsync()
    {
        var reservationId = Guid.CreateVersion7();
        var approvalRequestId = Guid.CreateVersion7();
        var start = NextMonthAt(10);
        await _executor.ExecuteAsync(new CreateQuickReservation
        {
            ReservationId = reservationId, RoomId = _approvalRoomId, ApprovalRequestId = approvalRequestId,
            OrganizerId = _organizerId, OrganizerName = "Organizer",
            StartTime = start, EndTime = start.AddHours(1), Purpose = "Board"
        });
        return (reservationId, approvalRequestId);
    }

    private async Task<Guid> CreateDraftAsync(Guid roomId, DateTime? start = null)
    {
        var reservationId = Guid.CreateVersion7();
        var startTime = start ?? NextMonthAt(11);
        await _executor.ExecuteAsync(new CreateReservationDraft
        {
            ReservationId = reservationId, RoomId = roomId, OrganizerId = _organizerId, OrganizerName = "Organizer",
            StartTime = startTime, EndTime = startTime.AddHours(1), Purpose = "Draft"
        });
        return reservationId;
    }

    // isAdmin stands for the authenticated Admin role, which the API copies onto the command
    private Task CancelAsync(Guid reservationId, Guid requestedBy, bool isAdmin = false) =>
        _executor.ExecuteAsync(new CancelReservation
        {
            ReservationId = reservationId, Reason = "No longer needed", RequestedByUserId = requestedBy,
            ActingUserIsAdmin = isAdmin
        });

    private Task HoldAsync(Guid reservationId, Guid requestedBy, bool isAdmin = false) =>
        _executor.ExecuteAsync(new CommitReservationHold
        {
            ReservationId = reservationId, RequestedByUserId = requestedBy, ActingUserIsAdmin = isAdmin
        });

    private Task RejectAsync(Guid reservationId, Guid approvalRequestId) =>
        _executor.ExecuteAsync(new RejectReservation
        {
            ReservationId = reservationId, RoomId = _approvalRoomId, ApprovalRequestId = approvalRequestId,
            Reason = "Not available", RejectedByUserId = _adminId
        });

    private Task DecideAsync(Guid approvalRequestId, Guid reservationId, ApprovalDecision decision, string? comment = null) =>
        _executor.ExecuteAsync(new RecordApprovalDecision
        {
            ApprovalRequestId = approvalRequestId, ReservationId = reservationId, ApproverId = _adminId,
            Decision = decision, Comment = comment
        });

    private async Task<ApprovalRequestState?> ApprovalStateAsync(Guid approvalRequestId)
    {
        var state = await _executor.GetTagStateAsync(
            new TagStateId(new ApprovalRequestTag(approvalRequestId), nameof(ApprovalRequestProjector)));
        return state.Payload as ApprovalRequestState;
    }

    private async Task<ReservationState?> ReservationStateAsync(Guid reservationId)
    {
        var state = await _executor.GetTagStateAsync(
            new TagStateId(new ReservationTag(reservationId), nameof(ReservationProjector)));
        return state.Payload as ReservationState;
    }

    private static DateTime NextMonthAt(int hour)
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, 1, hour, 0, 0, DateTimeKind.Utc).AddMonths(1).AddDays(2);
    }

    private static IReadOnlyList<string> PoliciesFor(string method, string route)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        var app = builder.Build();
        var api = app.MapGroup("/api");
        api.MapRoomEndpoints();
        api.MapReservationEndpoints();
        api.MapApprovalEndpoints();
        api.MapTestDataEndpoints();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == route
                && e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains(method) == true);

        return endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(data => data.Policy)
            .OfType<string>()
            .ToList();
    }
}
