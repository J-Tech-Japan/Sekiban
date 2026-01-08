using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.Tags;

/// <summary>
///     Tag representing room activity for a specific date.
///     1 room + 1 day = 1 tag for conflict detection.
///     This is the consistency boundary for reservation conflicts.
/// </summary>
public record RoomDailyActivityTag(Guid RoomId, DateOnly Date) : ITagGroup<RoomDailyActivityTag>
{
    public bool IsConsistencyTag() => true;
    public static string TagGroupName => "RoomDailyActivity";

    public static RoomDailyActivityTag FromContent(string content)
    {
        // Format: "RoomId_Date" (using underscore as separator since pipe is not allowed)
        // Find the last underscore that separates the GUID from the date
        var lastUnderscoreIndex = content.LastIndexOf('_');
        var roomIdPart = content[..lastUnderscoreIndex];
        var datePart = content[(lastUnderscoreIndex + 1)..];
        return new RoomDailyActivityTag(
            Guid.Parse(roomIdPart),
            DateOnly.Parse(datePart));
    }

    public string GetTagContent() => $"{RoomId}_{Date:yyyy-MM-dd}";

    /// <summary>
    ///     Creates tags for all days that a reservation spans.
    /// </summary>
    public static IEnumerable<RoomDailyActivityTag> CreateTagsForTimeRange(
        Guid roomId, DateTime startTime, DateTime endTime)
    {
        var startDate = DateOnly.FromDateTime(startTime);
        var endDate = DateOnly.FromDateTime(endTime);

        // If reservation ends at midnight, don't include that day
        if (endTime.TimeOfDay == TimeSpan.Zero && endDate > startDate)
        {
            endDate = endDate.AddDays(-1);
        }

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            yield return new RoomDailyActivityTag(roomId, date);
        }
    }
}
