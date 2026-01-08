using System.Security.Claims;
using System.Text.Json.Serialization;
using Dcb.EventSource.MeetingRoom.ApprovalRequest;
using Dcb.EventSource.MeetingRoom.Queries;
using Dcb.EventSource.MeetingRoom.Reservation;
using Dcb.MeetingRoomModels.Events.ApprovalRequest;
using Dcb.MeetingRoomModels.States.ApprovalRequest;
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
        return Results.Ok(result.Items);
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

        string? sortableUniqueId = recordResult.SortableUniqueId;

        if (request.Decision == ApprovalDecision.Approved)
        {
            var confirmResult = await executor.ExecuteAsync(new ConfirmReservation
            {
                ReservationId = pending.ReservationId,
                RoomId = pending.RoomId
            });
            sortableUniqueId = confirmResult.SortableUniqueId ?? sortableUniqueId;
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
            sortableUniqueId = rejectResult.SortableUniqueId ?? sortableUniqueId;
        }

        return Results.Ok(new
        {
            success = true,
            approvalRequestId,
            reservationId = pending.ReservationId,
            decision = request.Decision.ToString(),
            sortableUniqueId
        });
    }
}

public record ApprovalDecisionRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] ApprovalDecision Decision,
    string? Comment);
