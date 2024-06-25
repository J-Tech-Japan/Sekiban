using Sekiban.Core.Aggregate;
namespace SekibanEventSourcingBasics.Domain.Aggregates.UserPoints;

public record UserPoint(string Name, string Email, int Point) : IAggregatePayload<UserPoint>
{
    public static UserPoint CreateInitialPayload(UserPoint? _) =>
        new(string.Empty, string.Empty, 0);
}
