using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;

namespace CompatibilityDomain;

public record RegisterAccount(Guid Id, string Name, DateTime RegisteredAt)
    : ICommandWithHandler<RegisterAccount, AccountProjector>
{
    public PartitionKeys SpecifyPartitionKeys(RegisterAccount command) =>
        PartitionKeys.Existing<AccountProjector>(command.Id, "compatibility");

    public ResultBox<EventOrNone> Handle(RegisterAccount command, ICommandContext<IAggregatePayload> context) =>
        EventOrNone.Event(new AccountRegistered(command.Name, command.RegisteredAt));
}

public record AccountRegistered(string Name, DateTime RegisteredAt) : IEventPayload;
public record Account(string Name, DateTime RegisteredAt) : IAggregatePayload;

public class AccountProjector : IAggregateProjector
{
    public string GetVersion() => "1.0.0";
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev) => ev.GetPayload() switch
    {
        AccountRegistered registered => new Account(registered.Name, registered.RegisteredAt),
        _ => payload
    };
}
