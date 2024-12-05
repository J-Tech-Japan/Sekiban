using ResultBoxes;
using Sekiban.Pure;
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
    public static Func<IAggregatePayload, IEvent, IAggregatePayload> Projector() =>
        (payload, ev) => (payload, ev.GetPayload()) switch
        {
            (EmptyAggregatePayload, UserRegistered registered) => new UnconfirmedUser(registered.Name, registered.Email),
            (UnconfirmedUser unconfirmedUser, UserConfirmed) => new ConfirmedUser(unconfirmedUser.Name, unconfirmedUser.Email),
            (ConfirmedUser confirmedUser, UserUnconfirmed) => new UnconfirmedUser(confirmedUser.Name, confirmedUser.Email),
            _ => payload
        };
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev) => (payload, ev.GetPayload()) switch
    {
        (EmptyAggregatePayload, UserRegistered registered) => new UnconfirmedUser(registered.Name, registered.Email),
        (UnconfirmedUser unconfirmedUser, UserConfirmed) => new ConfirmedUser(unconfirmedUser.Name, unconfirmedUser.Email),
        (ConfirmedUser confirmedUser, UserUnconfirmed) => new UnconfirmedUser(confirmedUser.Name, confirmedUser.Email),
        _ => payload
    };
    public string GetVersion() => "1.0.1";
}
public record Branch(string Name) : IAggregatePayload;
public record BranchCreated(string Name) : IEventPayload;
public class BranchProjector : IAggregateProjector
{
    public static Func<IAggregatePayload, IEvent, IAggregatePayload> Projector() =>
        (payload, ev) => (payload, ev.GetPayload()) switch
        {
            (EmptyAggregatePayload, BranchCreated created) => new Branch(created.Name),
            _ => payload
        };
    public IAggregatePayload Project(IAggregatePayload payload, IEvent ev) =>
        (payload, ev.GetPayload()) switch
        {
            (EmptyAggregatePayload, BranchCreated created) => new Branch(created.Name),
            _ => payload
        };
}

public record RegisterBranch(string Name)
    : ICommand<RegisterBranch, BranchProjector>
{
    public static PartitionKeys SpecifyPartitionKeys(RegisterBranch input) => PartitionKeys.Generate();
    // public static ResultBox<EventOrNone> Handle(
    //     RegisterBranch input,
    //     Func<Aggregate<EmptyAggregatePayload>> stateFunc) =>
    //     EventOrNone.Event(new BranchCreated(input.Name));
    public PartitionKeys SpecifyPartitionKeys() => PartitionKeys.Generate();
    public ResultBox<EventOrNone> Handle(ICommandContext context) => EventOrNone.Event(new BranchCreated(Name));
    public static ResultBox<EventOrNone> Handle(RegisterBranch command, ICommandContext context)  => 
          EventOrNone.Event(new BranchCreated(command.Name));
    public Func<RegisterBranch, ICommandContext, ResultBox<EventOrNone>> Handler => 
        (command, context) => EventOrNone.Event(new BranchCreated(command.Name));
    public Func<RegisterBranch, ICommandContext, ResultBox<EventOrNone>> Handler2() => 
        (command, context) => EventOrNone.Event(new BranchCreated(command.Name));
}