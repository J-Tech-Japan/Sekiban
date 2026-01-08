using System.Security.Claims;
using Dcb.EventSource.MeetingRoom.Queries;
using Dcb.EventSource.MeetingRoom.Reservation;
using Dcb.Interactions.Workflows.Reservation;
using Dcb.MeetingRoomModels.Events.Reservation;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Dcb;
namespace SekibanDcbOrleans.ApiService.Endpoints;

public static class ReservationEndpoints
{
    public static void MapReservationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/reservations")
            .WithTags("MeetingRoom - Reservations")
            .RequireAuthorization();

        // Query endpoints
        group.MapGet("/", GetReservationListAsync)
            .WithName("GetReservations");

        group.MapGet("/by-room/{roomId:guid}", GetReservationsByRoomAsync)
            .WithName("GetReservationsByRoom");

        // Command endpoints - individual commands
        group.MapPost("/draft", CreateReservationDraftAsync)
            .WithName("CreateReservationDraft");

        group.MapPost("/{reservationId:guid}/hold", CommitReservationHoldAsync)
            .WithName("CommitReservationHold");

        group.MapPost("/{reservationId:guid}/confirm", ConfirmReservationAsync)
            .WithName("ConfirmReservation")
            .RequireAuthorization("AdminOnly");

        group.MapPost("/{reservationId:guid}/cancel", CancelReservationAsync)
            .WithName("CancelReservation");

        group.MapPost("/{reservationId:guid}/reject", RejectReservationAsync)
            .WithName("RejectReservation");

        // Workflow endpoints
        group.MapPost("/quick", QuickReservationAsync)
            .WithName("QuickReservation");
    }

    private static async Task<IResult> GetReservationListAsync(
        [FromQuery] string? waitForSortableUniqueId,
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromServices] ISekibanExecutor executor)
    {
        pageNumber ??= 1;
        pageSize ??= 100;
        var query = new GetReservationListQuery
        {
            WaitForSortableUniqueId = waitForSortableUniqueId,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
        var result = await executor.QueryAsync(query);
        return Results.Ok(result.Items);
    }

    private static async Task<IResult> GetReservationsByRoomAsync(
        Guid roomId,
        [FromQuery] string? waitForSortableUniqueId,
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromServices] ISekibanExecutor executor)
    {
        pageNumber ??= 1;
        pageSize ??= 100;
        var query = new GetReservationListQuery
        {
            WaitForSortableUniqueId = waitForSortableUniqueId,
            PageNumber = pageNumber,
            PageSize = pageSize,
            RoomId = roomId
        };
        var result = await executor.QueryAsync(query);
        return Results.Ok(result.Items);
    }

    private static async Task<IResult> CreateReservationDraftAsync(
        [FromBody] CreateReservationDraft command,
        HttpContext httpContext,
        [FromServices] ISekibanExecutor executor)
    {
        var displayName = httpContext.User.FindFirstValue("display_name")
            ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
            ?? command.OrganizerName
            ?? "Unknown User";
        var updatedCommand = command with { OrganizerName = displayName };
        var result = await executor.ExecuteAsync(updatedCommand);
        var createdEvent = result.Events.FirstOrDefault(m => m.Payload is ReservationDraftCreated)?.Payload.As<ReservationDraftCreated>();
        return Results.Ok(new
        {
            success = true,
            eventId = result.EventId,
            reservationId = createdEvent?.ReservationId ?? updatedCommand.ReservationId,
            organizerName = displayName,
            sortableUniqueId = result.SortableUniqueId
        });
    }

    private static async Task<IResult> CommitReservationHoldAsync(
        Guid reservationId,
        [FromBody] CommitReservationHoldRequest request,
        [FromServices] ISekibanExecutor executor)
    {
        var command = new CommitReservationHold
        {
            ReservationId = reservationId,
            RoomId = request.RoomId,
            RequiresApproval = request.RequiresApproval,
            ApprovalRequestId = request.ApprovalRequestId,
            ApprovalRequestComment = request.ApprovalRequestComment
        };
        var result = await executor.ExecuteAsync(command);
        return Results.Ok(new
        {
            success = true,
            eventId = result.EventId,
            reservationId = reservationId,
            sortableUniqueId = result.SortableUniqueId
        });
    }

    private static async Task<IResult> ConfirmReservationAsync(
        Guid reservationId,
        [FromBody] ConfirmReservationRequest request,
        [FromServices] ISekibanExecutor executor)
    {
        var command = new ConfirmReservation
        {
            ReservationId = reservationId,
            RoomId = request.RoomId
        };
        var result = await executor.ExecuteAsync(command);
        return Results.Ok(new
        {
            success = true,
            eventId = result.EventId,
            reservationId = reservationId,
            sortableUniqueId = result.SortableUniqueId
        });
    }

    private static async Task<IResult> CancelReservationAsync(
        Guid reservationId,
        [FromBody] CancelReservationRequest request,
        [FromServices] ISekibanExecutor executor)
    {
        var command = new CancelReservation
        {
            ReservationId = reservationId,
            RoomId = request.RoomId,
            Reason = request.Reason
        };
        var result = await executor.ExecuteAsync(command);
        return Results.Ok(new
        {
            success = true,
            eventId = result.EventId,
            reservationId = reservationId,
            sortableUniqueId = result.SortableUniqueId
        });
    }

    private static async Task<IResult> RejectReservationAsync(
        Guid reservationId,
        [FromBody] RejectReservationRequest request,
        [FromServices] ISekibanExecutor executor)
    {
        var command = new RejectReservation
        {
            ReservationId = reservationId,
            RoomId = request.RoomId,
            ApprovalRequestId = request.ApprovalRequestId,
            Reason = request.Reason
        };
        var result = await executor.ExecuteAsync(command);
        return Results.Ok(new
        {
            success = true,
            eventId = result.EventId,
            reservationId = reservationId,
            sortableUniqueId = result.SortableUniqueId
        });
    }

    private static async Task<IResult> QuickReservationAsync(
        [FromBody] QuickReservationRequest request,
        HttpContext httpContext,
        [FromServices] ISekibanExecutor executor)
    {
        // Get user ID from JWT claims
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var displayName = httpContext.User.FindFirstValue("display_name")
            ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
            ?? "Unknown User";
        if (userId == null || !Guid.TryParse(userId, out var organizerId))
        {
            // Use a default or generate one for demo purposes
            organizerId = Guid.CreateVersion7();
        }

        var workflow = new QuickReservationWorkflow(executor);
        var result = await workflow.ExecuteAsync(
            request.RoomId,
            organizerId,
            displayName,
            request.StartTime,
            request.EndTime,
            request.Purpose,
            request.ApprovalRequestComment);

        return Results.Ok(new
        {
            success = true,
            reservationId = result.ReservationId,
            organizerId = organizerId,
            organizerName = displayName,
            sortableUniqueId = result.SortableUniqueId,
            requiresApproval = result.RequiresApproval,
            approvalRequestId = result.ApprovalRequestId
        });
    }
}

// Request DTOs
public record CommitReservationHoldRequest(
    Guid RoomId,
    bool RequiresApproval,
    Guid? ApprovalRequestId,
    string? ApprovalRequestComment);
public record ConfirmReservationRequest(Guid RoomId);
public record CancelReservationRequest(Guid RoomId, string Reason);
public record RejectReservationRequest(Guid RoomId, Guid ApprovalRequestId, string Reason);
public record QuickReservationRequest(
    Guid RoomId,
    DateTime StartTime,
    DateTime EndTime,
    string Purpose,
    string? ApprovalRequestComment);
