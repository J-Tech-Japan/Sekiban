using Orleans;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using SharedDomain.Aggregates.User.Events;

namespace SharedDomain.Aggregates.User.Commands;

[GenerateSerializer]
public record UpdateUserName(
    [property: Id(0)] Guid UserId,
    [property: Id(1)] string NewName) : ICommandWithHandler<UpdateUserName, UserProjector>
{
    public PartitionKeys SpecifyPartitionKeys(UpdateUserName command) => 
        PartitionKeys.Existing<UserProjector>(command.UserId);

    public ResultBox<EventOrNone> Handle(UpdateUserName command, ICommandContext<IAggregatePayload> context)
    {
        return context.GetAggregate()
            .Conveyor(aggregate =>
            {
                var user = aggregate.Payload as User;
                
                if (user == null || user.UserId == Guid.Empty)
                {
                    return ResultBox<EventOrNone>.FromException(new InvalidOperationException("User does not exist"));
                }

                if (user.Name != command.NewName)
                {
                    return EventOrNone.Event(new UserNameChanged(command.UserId, command.NewName));
                }
                
                return EventOrNone.None;
            });
    }

    public static ResultBox<UnitValue> ValidateCommand(UpdateUserName command)
    {
        if (command.UserId == Guid.Empty)
            return ResultBox<UnitValue>.FromException(new ArgumentException("UserId cannot be empty"));
        if (string.IsNullOrWhiteSpace(command.NewName))
            return ResultBox<UnitValue>.FromException(new ArgumentException("NewName cannot be empty"));
        
        return ResultBox.UnitValue;
    }
}