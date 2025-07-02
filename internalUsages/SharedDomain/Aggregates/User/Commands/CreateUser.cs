using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using SharedDomain.Aggregates.User.Events;

namespace SharedDomain.Aggregates.User.Commands;

public record CreateUser(
    Guid UserId,
    string Name,
    string Email) : ICommandWithHandler<CreateUser, UserProjector>
{
    public PartitionKeys SpecifyPartitionKeys(CreateUser command) => 
        PartitionKeys.Existing<UserProjector>(command.UserId);

    public ResultBox<EventOrNone> Handle(CreateUser command, ICommandContext<IAggregatePayload> context)
    {
        return context.GetAggregate()
            .Conveyor(aggregate =>
            {
                var user = aggregate.Payload as User ?? new User(Guid.Empty, string.Empty, string.Empty);
                
                if (user.UserId != Guid.Empty)
                {
                    return ResultBox<EventOrNone>.FromException(new InvalidOperationException("User already exists"));
                }

                return EventOrNone.Event(new UserCreated(command.UserId, command.Name, command.Email));
            });
    }

    public static ResultBox<UnitValue> ValidateCommand(CreateUser command)
    {
        if (command.UserId == Guid.Empty)
            return ResultBox<UnitValue>.FromException(new ArgumentException("UserId cannot be empty"));
        if (string.IsNullOrWhiteSpace(command.Name))
            return ResultBox<UnitValue>.FromException(new ArgumentException("Name cannot be empty"));
        if (string.IsNullOrWhiteSpace(command.Email))
            return ResultBox<UnitValue>.FromException(new ArgumentException("Email cannot be empty"));
        
        return ResultBox.UnitValue;
    }
}