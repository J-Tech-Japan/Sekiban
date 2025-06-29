using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
namespace Pure.Domain;

public class UserProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev) => (payload, ev.GetPayload()) switch
    {
        (EmptyAggregatePayload, UserRegistered registered) => new UnconfirmedUser(registered.Name, registered.Email),
        (UnconfirmedUser unconfirmedUser, UserConfirmed) => new ConfirmedUser(
            unconfirmedUser.Name,
            unconfirmedUser.Email),
        (ConfirmedUser confirmedUser, UserUnconfirmed) => new UnconfirmedUser(confirmedUser.Name, confirmedUser.Email),
        (ConfirmedUser confirmedUser, UserNameUpdated updated) => confirmedUser with { Name = updated.NewName },
        (UnconfirmedUser unconfirmedUser, UserNameUpdated updated) => unconfirmedUser with { Name = updated.NewName },
        _ => payload
    };
    public string GetVersion() => "1.0.1";
}
