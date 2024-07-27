using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using System.ComponentModel.DataAnnotations;
using ResultBoxes;

namespace AspireAndSekibanSample.Domain.Aggregates.AccountUsers;

public record AccountUser([property:Required]string Name, [property:Required,EmailAddress]string Email) : IAggregatePayload<AccountUser>
{
    public static AccountUser CreateInitialPayload(AccountUser? _) => new(string.Empty, string.Empty);
}
public record AccountUserAdded(string Name, string Email) : IEventPayload<AccountUser, AccountUserAdded>
{
    public static AccountUser OnEvent(AccountUser aggregatePayload, Event<AccountUserAdded> ev) => aggregatePayload with
    {
        Name = ev.Payload.Name,
        Email = ev.Payload.Email
    };
}
public record AccountUserNameChanged(string Name, string Note) : IEventPayload<AccountUser, AccountUserNameChanged>
{
    public static AccountUser OnEvent(AccountUser aggregatePayload, Event<AccountUserNameChanged> ev) => aggregatePayload with
    {
        Name = ev.Payload.Name
    };
}

public record CreateAccountUser([property:Required]string Name, [property:Required,EmailAddress]string Email) : ICommandWithHandler<AccountUser, CreateAccountUser>
{
    public Guid GetAggregateId() => Guid.NewGuid();

    public static ResultBox<UnitValue> HandleCommand(CreateAccountUser command, ICommandContext<AccountUser> context)
        => context.AppendEvent(new AccountUserAdded(command.Name, command.Email));
}

public record ChangeAccountUserName(Guid AccountUserId, [property:Required]string Name) : ICommandWithHandler<AccountUser, ChangeAccountUserName>
{
    public Guid GetAggregateId() => AccountUserId;
    public static ResultBox<UnitValue> HandleCommand(ChangeAccountUserName command, ICommandContext<AccountUser> context)
        => context.AppendEvent(new AccountUserAdded(command.Name, "Note"));
}