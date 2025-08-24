using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using SharedDomain.Aggregates.User.Events;
namespace SharedDomain.Aggregates.User.Commands;

[GenerateSerializer]
public record UpdateUserEmail(
    [property: Id(0)]
    Guid UserId,
    [property: Id(1)]
    string NewEmail) : ICommandWithHandler<UpdateUserEmail, UserProjector>
{
    public PartitionKeys SpecifyPartitionKeys(UpdateUserEmail command) =>
        PartitionKeys.Existing<UserProjector>(command.UserId);

    public ResultBox<EventOrNone> Handle(UpdateUserEmail command, ICommandContext<IAggregatePayload> context)
    {
        return context
            .GetAggregate()
            .Conveyor(aggregate =>
            {
                var user = aggregate.Payload as User;

                if (user == null || user.UserId == Guid.Empty)
                {
                    return ResultBox<EventOrNone>.FromException(new InvalidOperationException("User does not exist"));
                }

                if (user.Email != command.NewEmail)
                {
                    return EventOrNone.Event(new UserEmailChanged(command.UserId, command.NewEmail));
                }

                return EventOrNone.None;
            });
    }

    public static ResultBox<UnitValue> ValidateCommand(UpdateUserEmail command)
    {
        if (command.UserId == Guid.Empty)
            return ResultBox<UnitValue>.FromException(new ArgumentException("UserId cannot be empty"));
        if (string.IsNullOrWhiteSpace(command.NewEmail))
            return ResultBox<UnitValue>.FromException(new ArgumentException("NewEmail cannot be empty"));

        return ResultBox.UnitValue;
    }
}
