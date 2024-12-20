﻿using Pure.Domain.Generated;
using ResultBoxes;
using Sekiban.Pure;
namespace Pure.Domain;

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
    public ResultBox<EventOrNone> Handle(RegisterBranch command, ICommandContext<IAggregatePayload> context) =>
        EventOrNone.Event(new BranchCreated(command.Name));
}
public record ChangeBranchName(Guid BranchId, string NameToChange)
    : ICommandWithHandler<ChangeBranchName, BranchProjector>
{
    public ResultBox<EventOrNone> Handle(ChangeBranchName command, ICommandContext<IAggregatePayload> context) =>
        context.AppendEvent(new BranchNameChanged(command.NameToChange));
    public PartitionKeys SpecifyPartitionKeys(ChangeBranchName command) =>
        PartitionKeys<BranchProjector>.Existing(BranchId);
}
public record RegisterBranch2(string Name) : ICommand;
public record RegisterBranch3(string Name) : ICommandWithAggregateRestriction<EmptyAggregatePayload>
{
}
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
public record ConfirmUser(Guid UserId) : ICommandWithHandler<ConfirmUser, UserProjector, UnconfirmedUser>
{
    public ResultBox<EventOrNone> Handle(ConfirmUser command, ICommandContext<UnconfirmedUser> context) =>
        EventOrNone.Event(new UserConfirmed());
    public PartitionKeys SpecifyPartitionKeys(ConfirmUser command) => PartitionKeys<UserProjector>.Existing(UserId);
}
public record RevokeUser(Guid UserId)
    : ICommandWithHandlerInjection<RevokeUser, UserProjector, RevokeUser.Injection, ConfirmedUser>
{
    public PartitionKeys SpecifyPartitionKeys(RevokeUser command) => PartitionKeys<UserProjector>.Existing(UserId);
    public ResultBox<EventOrNone> Handle(
        RevokeUser command,
        Injection injection,
        ICommandContext<ConfirmedUser> context) =>
        context
            .GetAggregate()
            .Conveyor(aggregate => injection.UserExists(aggregate.PartitionKeys.AggregateId).ToResultBox())
            .Verify(
                exists => exists
                    ? ExceptionOrNone.None
                    : ExceptionOrNone.FromException(new ApplicationException("user already exists")))
            .Conveyor(_ => EventOrNone.Event(new UserUnconfirmed()));
    public record Injection(Func<Guid, bool> UserExists);
}
public class Test
{
    public void Test1()
    {
        var commandExecutor = new CommandExecutor();
        commandExecutor.Execute(new RegisterBranch("name"));
        commandExecutor.Execute(
            new RegisterUser("tomo", "tomo@example.com"),
            new RegisterUser.Injection(email => false));
        commandExecutor.Execute(new ConfirmUser(Guid.CreateVersion7()));
    }
}
// public static class Extensions
// {
//     public static Task<ResultBox<CommandResponse>> ExecuteFunction(
//         this CommandExecutor executor,
//         ConfirmUser command,
//         IAggregateProjector projector,
//         Func<ConfirmUser, PartitionKeys> specifyPartitionKeys,
//         Func<ConfirmUser, ICommandContext<UnconfirmedUser>, ResultBox<EventOrNone>> handler) =>
//         executor.ExecuteFunction<ConfirmUser, UnconfirmedUser>(
//             command,
//             (command as ICommandGetProjector).GetProjector(),
//             command.SpecifyPartitionKeys,
//             command.Handle);
// }
// now writing manually, but it will be generated by the source generator
// public class DomainEventTypes : IEventTypes
// {
//     public ResultBox<IEvent> GenerateTypedEvent(
//         IEventPayload payload,
//         PartitionKeys partitionKeys,
//         string sortableUniqueId,
//         int version) => payload switch
//     {
//         UserRegistered userRegistered => new Event<UserRegistered>(
//             userRegistered,
//             partitionKeys,
//             sortableUniqueId,
//             version),
//         UserConfirmed userConfirmed => new Event<UserConfirmed>(
//             userConfirmed,
//             partitionKeys,
//             sortableUniqueId,
//             version),
//         UserUnconfirmed userUnconfirmed => new Event<UserUnconfirmed>(
//             userUnconfirmed,
//             partitionKeys,
//             sortableUniqueId,
//             version),
//         BranchCreated branchCreated => new Event<BranchCreated>(
//             branchCreated,
//             partitionKeys,
//             sortableUniqueId,
//             version),
//         BranchNameChanged branchNameChanged => new Event<BranchNameChanged>(
//             branchNameChanged,
//             partitionKeys,
//             sortableUniqueId,
//             version),
//         _ => ResultBox<IEvent>.FromException(
//             new SekibanEventTypeNotFoundException($"Event Type {payload.GetType().Name} Not Found"))
//     };
// }
