using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
namespace Pure.Domain;

public record RegisterUser(string Name, string Email)
    : ICommandWithHandlerInjection<RegisterUser, UserProjector, RegisterUser.Injection>
{
    public PartitionKeys SpecifyPartitionKeys(RegisterUser command) => PartitionKeys<UserProjector>.Generate();
    public ResultBox<EventOrNone> Handle(
        RegisterUser command,
        Injection injection,
        ICommandContext<IAggregatePayload> context) =>
        ResultBox
            .Start
            .Conveyor(m => injection.EmailExists(command.Email).ToResultBox())
            .Verify(
                exists => exists
                    ? ExceptionOrNone.FromException(new ApplicationException("Email already exists"))
                    : ExceptionOrNone.None)
            .Conveyor(_ => EventOrNone.Event(new UserRegistered(command.Name, command.Email)));
    public record Injection(Func<string, bool> EmailExists);
}