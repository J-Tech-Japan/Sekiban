using ResultBoxes;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
namespace SharedDomain.Aggregates.User.Queries;

[GenerateSerializer(GenerateFieldIds = GenerateFieldIds.PublicProperties)]
public record UserStatisticsQuery :
    IMultiProjectionQuery<AggregateListProjector<UserProjector>, UserStatisticsQuery,
        UserStatisticsQuery.UserStatistics>,
    IWaitForSortableUniqueId
{

    public static ResultBox<UserStatistics> HandleQuery(
        MultiProjectionState<AggregateListProjector<UserProjector>> projection,
        UserStatisticsQuery query,
        IQueryContext context)
    {
        var users = projection
            .Payload
            .Aggregates
            .Select(m => m.Value.GetPayload())
            .Where(payload => payload is User)
            .Cast<User>()
            .ToList();

        var totalUsers = users.Count;
        var usersWithGmailCount = users.Count(u => u.Email.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase));
        var averageNameLength = users.Any() ? users.Average(u => u.Name.Length) : 0;
        var longestUserName = users.OrderByDescending(u => u.Name.Length).FirstOrDefault()?.Name ?? "";

        return ResultBox<UserStatistics>.FromValue(
            new UserStatistics(totalUsers, usersWithGmailCount, averageNameLength, longestUserName));
    }
    public string? WaitForSortableUniqueId { get; set; }

    [GenerateSerializer]
    public record UserStatistics(int TotalUsers, int UsersWithGmail, double AverageNameLength, string LongestUserName);
}
