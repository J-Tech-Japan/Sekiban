using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Events;
namespace Pure.Domain;

public record ConfirmUser(Guid UserId) : ICommandWithHandler<ConfirmUser, UserProjector, UnconfirmedUser>
{
    public ResultBox<EventOrNone> Handle(ConfirmUser command, ICommandContext<UnconfirmedUser> context) =>
        EventOrNone.Event(new UserConfirmed());
    public PartitionKeys SpecifyPartitionKeys(ConfirmUser command) => PartitionKeys<UserProjector>.Existing(UserId);
}
