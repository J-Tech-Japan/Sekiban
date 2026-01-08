using System.Security.Claims;
using System.Text.Json.Serialization;
using Dcb.EventSource.MeetingRoom.ApprovalRequest;
using Dcb.EventSource.MeetingRoom.Queries;
using Dcb.EventSource.MeetingRoom.Reservation;
using Dcb.EventSource.MeetingRoom.Room;
using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Dcb.MeetingRoomModels.States.ApprovalRequest;
using Dcb.MeetingRoomModels.States.Reservation;
using Dcb.MeetingRoomModels.States.Room;
using Dcb.MeetingRoomModels.Tags;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Dcb;
using Sekiban.Dcb.Tags;
namespace SekibanDcbOrleans.ApiService.Endpoints;

public static class ApprovalEndpoints
{
    public static void MapApprovalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/approvals")
            .WithTags("MeetingRoom - Approvals")
            .RequireAuthorization("AdminOnly");

        group.MapGet("/", GetApprovalInboxAsync)
            .WithName("GetApprovalInbox");

        group.MapPost("/{approvalRequestId:guid}/decision", RecordApprovalDecisionAsync)
            .WithName("RecordApprovalDecision");
    }

    private static async Task<IResult> GetApprovalInboxAsync(
        [FromQuery] bool? pendingOnly,
        [FromQuery] string? waitForSortableUniqueId,
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromServices] ISekibanExecutor executor)
    {
        var query = new GetApprovalInboxQuery
        {
            WaitForSortableUniqueId = waitForSortableUniqueId,
            PageNumber = pageNumber ?? 1,
            PageSize = pageSize ?? 100,
            PendingOnly = pendingOnly ?? true
        };
        var result = await executor.QueryAsync(query);
        var items = new List<ApprovalInboxViewItem>();

        foreach (var item in result.Items)
        {
            var roomId = item.RoomId;
            string? roomName = null;
            Guid? organizerId = null;
            string? organizerName = null;
            string? purpose = null;
            DateTime? startTime = null;
            DateTime? endTime = null;

            if (item.ReservationId != Guid.Empty)
            {
                var reservationTag = new ReservationTag(item.ReservationId);
                var reservationState = await executor.GetTagStateAsync(
                    new TagStateId(reservationTag, nameof(ReservationProjector)));

                switch (reservationState.Payload)
                {
                    case ReservationState.ReservationDraft draft:
                        roomId = draft.RoomId;
                        organizerId = draft.OrganizerId;
                        organizerName = draft.OrganizerName;
                        purpose = draft.Purpose;
                        startTime = draft.StartTime;
                        endTime = draft.EndTime;
                        break;
                    case ReservationState.ReservationHeld held:
                        roomId = held.RoomId;
                        organizerId = held.OrganizerId;
                        organizerName = held.OrganizerName;
                        purpose = held.Purpose;
                        startTime = held.StartTime;
                        endTime = held.EndTime;
                        break;
                    case ReservationState.ReservationConfirmed confirmed:
                        roomId = confirmed.RoomId;
                        organizerId = confirmed.OrganizerId;
                        organizerName = confirmed.OrganizerName;
                        purpose = confirmed.Purpose;
                        startTime = confirmed.StartTime;
                        endTime = confirmed.EndTime;
                        break;
                }
            }

            if (roomId != Guid.Empty)
            {
                var roomTag = new RoomTag(roomId);
                var roomState = await executor.GetTagStateAsync(
                    new TagStateId(roomTag, nameof(RoomProjector)));
                if (roomState.Payload is RoomState room)
                {
                    roomName = room.Name;
                }
            }

            items.Add(new ApprovalInboxViewItem(
                item.ApprovalRequestId,
                item.ReservationId,
                roomId,
                roomName,
                item.RequesterId,
                item.RequestComment,
                organizerId,
                organizerName,
                purpose,
                startTime,
                endTime,
                item.ApproverIds,
                item.RequestedAt,
                item.Status));
        }

        return Results.Ok(items);
    }

    private static async Task<IResult> RecordApprovalDecisionAsync(
        Guid approvalRequestId,
        [FromBody] ApprovalDecisionRequest request,
        HttpContext httpContext,
        [FromServices] ISekibanExecutor executor)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null || !Guid.TryParse(userId, out var approverId))
        {
            return Results.BadRequest("Approver identity is invalid");
        }

        var tag = new ApprovalRequestTag(approvalRequestId);
        var approvalState = await executor.GetTagStateAsync(new TagStateId(tag, nameof(ApprovalRequestProjector)));
        if (approvalState.Payload is not ApprovalRequestState.ApprovalRequestPending pending)
        {
            return Results.BadRequest("Approval request is not pending");
        }

        var recordResult = await executor.ExecuteAsync(new RecordApprovalDecision
        {
            ApprovalRequestId = approvalRequestId,
            ReservationId = pending.ReservationId,
            ApproverId = approverId,
            Decision = request.Decision,
            Comment = request.Comment
        });

        var approvalSortableUniqueId = recordResult.SortableUniqueId;
        string? reservationSortableUniqueId = null;

        if (request.Decision == ApprovalDecision.Approved)
        {
            var confirmResult = await executor.ExecuteAsync(new ConfirmReservation
            {
                ReservationId = pending.ReservationId,
                RoomId = pending.RoomId
            });
            reservationSortableUniqueId = confirmResult.SortableUniqueId;
        }
        else if (request.Decision == ApprovalDecision.Rejected)
        {
            var rejectResult = await executor.ExecuteAsync(new RejectReservation
            {
                ReservationId = pending.ReservationId,
                RoomId = pending.RoomId,
                ApprovalRequestId = approvalRequestId,
                Reason = request.Comment ?? "Rejected"
            });
            reservationSortableUniqueId = rejectResult.SortableUniqueId;
        }

        return Results.Ok(new
        {
            success = true,
            approvalRequestId,
            reservationId = pending.ReservationId,
            decision = request.Decision.ToString(),
            sortableUniqueId = approvalSortableUniqueId,
            reservationSortableUniqueId
        });
    }
}

public record ApprovalInboxViewItem(
    Guid ApprovalRequestId,
    Guid ReservationId,
    Guid RoomId,
    string? RoomName,
    Guid RequesterId,
    string? RequestComment,
    Guid? OrganizerId,
    string? OrganizerName,
    string? Purpose,
    DateTime? StartTime,
    DateTime? EndTime,
    List<Guid> ApproverIds,
    DateTime RequestedAt,
    string Status);

public record ApprovalDecisionRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ApprovalDecision Decision,
    string? Comment);
