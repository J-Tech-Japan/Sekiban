using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;

namespace Pure.Domain;

[GenerateSerializer]
public record UpdateUserName(Guid UserId, string NewName) : ICommandWithHandler<UpdateUserName, UserProjector, ConfirmedUser>
{
    public PartitionKeys SpecifyPartitionKeys(UpdateUserName command) =>
        PartitionKeys<UserProjector>.Existing(command.UserId);

    public ResultBox<EventOrNone> Handle(UpdateUserName command, ICommandContext<ConfirmedUser> context)
    {
        var user = context.GetAggregate().UnwrapBox().Payload;
        
        // If the name is the same, return None (no event)
        if (user.Name == command.NewName)
        {
            return EventOrNone.None;
        }
        
        return EventOrNone.Event(new UserNameUpdated(user.Name, command.NewName));
    }
}

[GenerateSerializer]
public record UserNameUpdated(string OldName, string NewName) : IEventPayload;