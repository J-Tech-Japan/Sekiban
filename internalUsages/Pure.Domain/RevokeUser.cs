using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Events;
namespace Pure.Domain;

public record RevokeUser(Guid UserId)
    : ICommandWithHandlerInjection<RevokeUser, UserProjector, RevokeUser.Injection, ConfirmedUser>
{
    public PartitionKeys SpecifyPartitionKeys(RevokeUser command) => PartitionKeys<UserProjector>.Existing(UserId);
    public ResultBox<EventOrNone> Handle(
        RevokeUser command,
        Injection injection,
        ICommandContext<ConfirmedUser> context) =>
        context
            .GetAggregate()
            .Conveyor(aggregate => injection.UserExists(aggregate.PartitionKeys.AggregateId).ToResultBox())
            .Verify(
                exists => exists
                    ? ExceptionOrNone.None
                    : ExceptionOrNone.FromException(new ApplicationException("user already exists")))
            .Conveyor(_ => EventOrNone.Event(new UserUnconfirmed()));
    public record Injection(Func<Guid, bool> UserExists);
}
