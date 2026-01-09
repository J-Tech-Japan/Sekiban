using Sekiban.Dcb.Tags;
namespace Dcb.MeetingRoomModels.States.UserMonthlyReservation;

public record UserMonthlyReservationState : ITagStatePayload
{
    public static UserMonthlyReservationState Empty => new();

    public Guid UserId { get; init; }
    public DateOnly Month { get; init; }
    public int TotalRequests { get; init; }
    public int RejectedRequests { get; init; }

    public int ActiveRequestCount => Math.Max(0, TotalRequests - RejectedRequests);

    public UserMonthlyReservationState RegisterRequest(Guid userId, DateOnly month)
    {
        var initialized = EnsureIdentity(userId, month);
        return initialized with { TotalRequests = initialized.TotalRequests + 1 };
    }

    public UserMonthlyReservationState RegisterRejection()
    {
        if (UserId == Guid.Empty || Month == default)
        {
            return this;
        }

        var nextRejected = RejectedRequests + 1;
        if (nextRejected > TotalRequests)
        {
            nextRejected = TotalRequests;
        }

        return this with { RejectedRequests = nextRejected };
    }

    private UserMonthlyReservationState EnsureIdentity(Guid userId, DateOnly month)
    {
        if (UserId != Guid.Empty && Month != default)
        {
            return this;
        }

        return this with { UserId = userId, Month = month };
    }
}
