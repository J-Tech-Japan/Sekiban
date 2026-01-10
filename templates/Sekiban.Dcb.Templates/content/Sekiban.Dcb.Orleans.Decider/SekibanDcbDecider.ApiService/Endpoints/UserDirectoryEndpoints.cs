using Dcb.EventSource.MeetingRoom.Queries;
using Dcb.EventSource.MeetingRoom.User;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Dcb;

namespace SekibanDcbDecider.ApiService.Endpoints;

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
        // Fetch user directory list
        var userDirectoryQuery = new GetUserDirectoryListQuery
        {
            WaitForSortableUniqueId = waitForSortableUniqueId,
            PageNumber = pageNumber ?? 1,
            PageSize = pageSize ?? 100,
            ActiveOnly = activeOnly ?? false
        };
        var userDirectoryResult = await executor.QueryAsync(userDirectoryQuery);

        // Fetch user access list to get roles
        var userAccessQuery = new GetUserAccessListQuery
        {
            PageNumber = 1,
            PageSize = 1000  // Get all user access records
        };
        var userAccessResult = await executor.QueryAsync(userAccessQuery);

        // Create a lookup for roles by userId
        var rolesLookup = userAccessResult.Items
            .ToDictionary(ua => ua.UserId, ua => ua.Roles);

        // Merge roles into user directory items
        var enrichedItems = userDirectoryResult.Items
            .Select(user => rolesLookup.TryGetValue(user.UserId, out var roles)
                ? user.WithRoles(roles)
                : user)
            .ToList();

        return Results.Ok(enrichedItems);
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
