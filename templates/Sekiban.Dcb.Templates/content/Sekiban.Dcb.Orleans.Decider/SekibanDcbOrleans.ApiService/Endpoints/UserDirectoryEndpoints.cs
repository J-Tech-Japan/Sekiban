using Dcb.EventSource.MeetingRoom.Queries;
using Dcb.EventSource.MeetingRoom.User;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Dcb;
namespace SekibanDcbOrleans.ApiService.Endpoints;

public static class UserDirectoryEndpoints
{
    public static void MapUserDirectoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/users")
            .WithTags("MeetingRoom - Users")
            .RequireAuthorization("AdminOnly");

        group.MapGet("/", GetUserDirectoryListAsync)
            .WithName("GetUserDirectoryList");

        group.MapPost("/{userId:guid}/monthly-limit", UpdateUserMonthlyLimitAsync)
            .WithName("UpdateUserMonthlyLimit");
    }

    private static async Task<IResult> GetUserDirectoryListAsync(
        [FromQuery] string? waitForSortableUniqueId,
        [FromQuery] int? pageNumber,
        [FromQuery] int? pageSize,
        [FromQuery] bool? activeOnly,
        [FromServices] ISekibanExecutor executor)
    {
        var query = new GetUserDirectoryListQuery
        {
            WaitForSortableUniqueId = waitForSortableUniqueId,
            PageNumber = pageNumber ?? 1,
            PageSize = pageSize ?? 100,
            ActiveOnly = activeOnly ?? false
        };
        var result = await executor.QueryAsync(query);
        return Results.Ok(result.Items);
    }

    private static async Task<IResult> UpdateUserMonthlyLimitAsync(
        Guid userId,
        [FromBody] UpdateMonthlyReservationLimitRequest request,
        [FromServices] ISekibanExecutor executor)
    {
        if (request.MonthlyReservationLimit <= 0)
        {
            return Results.BadRequest("Monthly reservation limit must be greater than zero.");
        }

        var result = await executor.ExecuteAsync(new UpdateUserMonthlyReservationLimit
        {
            UserId = userId,
            MonthlyReservationLimit = request.MonthlyReservationLimit
        });

        return Results.Ok(new
        {
            success = true,
            userId,
            monthlyReservationLimit = request.MonthlyReservationLimit,
            sortableUniqueId = result.SortableUniqueId
        });
    }
}

public record UpdateMonthlyReservationLimitRequest(int MonthlyReservationLimit);
