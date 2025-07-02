using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using SharedDomain.Aggregates.User.Events;

namespace SharedDomain.Aggregates.User;

public class UserProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev)
    {
        var user = payload as User ?? new User(Guid.Empty, string.Empty, string.Empty);
        
        return ev switch
        {
            Event<UserCreated> e => new User(e.Payload.UserId, e.Payload.Name, e.Payload.Email),
            Event<UserNameChanged> e => user with { Name = e.Payload.NewName },
            Event<UserEmailChanged> e => user with { Email = e.Payload.NewEmail },
            _ => user
        };
    }
}

public record User(
    Guid UserId,
    string Name,
    string Email) : IAggregatePayload;