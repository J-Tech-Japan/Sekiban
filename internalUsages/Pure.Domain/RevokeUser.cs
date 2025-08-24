using ResultBoxes;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
namespace Pure.Domain;

public record RevokeUser(Guid UserId) : ICommandWithHandler<RevokeUser, UserProjector, ConfirmedUser>
{
    public PartitionKeys SpecifyPartitionKeys(RevokeUser command) => PartitionKeys<UserProjector>.Existing(UserId);
    public ResultBox<EventOrNone> Handle(RevokeUser command, ICommandContext<ConfirmedUser> context) =>
        context
            .GetAggregate()
            // .Conveyor(aggregate => injection.UserExists(aggregate.PartitionKeys.AggregateId).ToResultBox())
            // .Verify(
            //     exists => exists
            //         ? ExceptionOrNone.None
            //         : ExceptionOrNone.FromException(new ApplicationException("user already exists")))
            .Conveyor(_ => EventOrNone.Event(new UserUnconfirmed()));
}
