using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.Exception;
namespace Pure.Domain;

public class Class1
{
}
public record UnconfirmedUser(string Name, string Email) : IAggregatePayload;
public record ConfirmedUser(string Name, string Email) : IAggregatePayload;
public record UserRegistered(string Name, string Email) : IEventPayload;
public record UserConfirmed : IEventPayload;
public record UserUnconfirmed : IEventPayload;
public class UserProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev) => (payload, ev.GetPayload()) switch
    {
        (EmptyAggregatePayload, UserRegistered registered) => new UnconfirmedUser(registered.Name, registered.Email),
        (UnconfirmedUser unconfirmedUser, UserConfirmed) => new ConfirmedUser(
            unconfirmedUser.Name,
            unconfirmedUser.Email),
        (ConfirmedUser confirmedUser, UserUnconfirmed) => new UnconfirmedUser(confirmedUser.Name, confirmedUser.Email),
        _ => payload
    };
    public string GetVersion() => "1.0.1";
    public static Func<IAggregatePayload, IEvent, IAggregatePayload> Projector() =>
        (payload, ev) => (payload, ev.GetPayload()) switch
        {
            (EmptyAggregatePayload, UserRegistered registered) => new UnconfirmedUser(
                registered.Name,
                registered.Email),
            (UnconfirmedUser unconfirmedUser, UserConfirmed) => new ConfirmedUser(
                unconfirmedUser.Name,
                unconfirmedUser.Email),
            (ConfirmedUser confirmedUser, UserUnconfirmed) => new UnconfirmedUser(
                confirmedUser.Name,
                confirmedUser.Email),
            _ => payload
        };
}
public record Branch(string Name) : IAggregatePayload;
public record BranchCreated(string Name) : IEventPayload;
public record BranchNameChanged(string Name) : IEventPayload;
public class BranchProjector : IAggregateProjector
{
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev) =>
        (payload, ev.GetPayload()) switch
        {
            (EmptyAggregatePayload, BranchCreated created) => new Branch(created.Name),
            (Branch branch, BranchNameChanged changed) => new Branch(changed.Name),
            _ => payload
        };
}
public record RegisterBranch(string Name) : ICommandWithHandler<RegisterBranch, BranchProjector>
{
    public PartitionKeys SpecifyPartitionKeys(RegisterBranch command) => PartitionKeys<BranchProjector>.Generate();
    public ResultBox<EventOrNone> Handle(RegisterBranch command, ICommandContext context) =>
        EventOrNone.Event(new BranchCreated(command.Name));
}
public record ChangeBranchName(Guid BranchId, string NameToChange)
    : ICommandWithHandler<ChangeBranchName, BranchProjector>
{
    public ResultBox<EventOrNone> Handle(ChangeBranchName command, ICommandContext context) =>
        context.AppendEvent(new BranchNameChanged(command.NameToChange));
    public PartitionKeys SpecifyPartitionKeys(ChangeBranchName command) =>
        PartitionKeys<BranchProjector>.Existing(BranchId);
}


public record RegisterCommand2(string Name, Guid BranchId, string TenantCode) : ICommand;
public class RegisterCommand2Handler : ICommandHandler<RegisterCommand2>, ICommandPartitionSpecifier<RegisterCommand2>
{
    public ResultBox<EventOrNone> Handle(RegisterCommand2 command, ICommandContext context) =>
        throw new NotImplementedException();
    public PartitionKeys SpecifyPartitionKeys(RegisterCommand2 command) => throw new NotImplementedException();
}
public record RegisterCommand3(string Name) : ICommand, ICommandHandler<RegisterCommand3>
{
    public ResultBox<EventOrNone> Handle(RegisterCommand3 command, ICommandContext context) =>
        EventOrNone.Event(new BranchCreated(command.Name));
}
public record RegisterUser(string Name, string Email)
    : ICommandWithHandlerInjection<RegisterUser, UserProjector, RegisterUser.Injection>
{
    public PartitionKeys SpecifyPartitionKeys(RegisterUser command) => PartitionKeys<UserProjector>.Generate();
    public ResultBox<EventOrNone> Handle(RegisterUser command, Injection injection, ICommandContext context) =>
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
public class Test
{
    public void Test1()
    {
        var commandExecutor = new CommandExecutor();
        commandExecutor.Execute(
            new RegisterCommand2("name", Guid.CreateVersion7(), "tenantCode"),
            new BranchProjector(),
            c => PartitionKeys.Existing(c.BranchId),
            (c, context) => EventOrNone.Event(new BranchCreated(c.Name)));

        commandExecutor.Execute(
            new RegisterCommand2("name", Guid.CreateVersion7(), "tenantCode"),
            new BranchProjector(),
            c => PartitionKeys.Existing<BranchProjector>(c.BranchId),
            (_, _) => EventOrNone.Event(new BranchCreated("name")));
        commandExecutor.Execute(new RegisterBranch("name"));
        commandExecutor.Execute(
            new RegisterUser("tomo", "tomo@example.com"),
            new RegisterUser.Injection(email => false));
    }
}
public class DomainEventTypes : IEventTypes
{
    public ResultBox<IEvent> GenerateTypedEvent(
        IEventPayload payload,
        PartitionKeys partitionKeys,
        string sortableUniqueId,
        long version) => payload switch
    {
        UserRegistered userRegistered => new Event<UserRegistered>(
            userRegistered,
            partitionKeys,
            sortableUniqueId,
            version),
        UserConfirmed userConfirmed => new Event<UserConfirmed>(
            userConfirmed,
            partitionKeys,
            sortableUniqueId,
            version),
        UserUnconfirmed userUnconfirmed => new Event<UserUnconfirmed>(
            userUnconfirmed,
            partitionKeys,
            sortableUniqueId,
            version),
        BranchCreated branchCreated => new Event<BranchCreated>(
            branchCreated,
            partitionKeys,
            sortableUniqueId,
            version),
        BranchNameChanged branchNameChanged => new Event<BranchNameChanged>(
            branchNameChanged,
            partitionKeys,
            sortableUniqueId,
            version),
        _ => ResultBox<IEvent>.FromException(
            new SekibanEventTypeNotFoundException($"Event Type {payload.GetType().Name} Not Found"))
    };
}
