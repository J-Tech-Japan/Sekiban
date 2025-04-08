using Microsoft.Extensions.DependencyInjection;
using Pure.Domain.Generated;
using ResultBoxes;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Events;
using Sekiban.Pure.Exceptions;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Repositories;
namespace Pure.Domain.Test;

public class SimpleEventSourcingTests
{
    [Fact]
    public async Task SimpleEventSourcing()
    {
        InMemorySekibanExecutor executor = new(
            PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options),
            new FunctionCommandMetadataProvider(() => "test"),
            new Repository(),
            new ServiceCollection().BuildServiceProvider());
        Assert.Empty(executor.Repository.Events);
        await executor.CommandAsync(new RegisterBranch("branch1"));

        Assert.Single(executor.Repository.Events);
        var last = executor.Repository.Events.Last();
        Assert.IsType<Event<BranchCreated>>(last);

        var aggregate = executor.Repository.Load(last.PartitionKeys, new BranchProjector()).UnwrapBox();
        var payload = aggregate.GetPayload();
        Assert.IsType<Branch>(payload);

        var userExecuted = await executor.CommandAsync(new RegisterUser("tomo", "tomo@example.com")).UnwrapBox();
        Assert.NotNull(userExecuted);
        var confirmResult = await executor.CommandAsync(new ConfirmUser(userExecuted.PartitionKeys.AggregateId));
        Assert.NotNull(confirmResult);
        Assert.True(confirmResult.IsSuccess);
        var confirmResult2 = await executor.CommandAsync(new ConfirmUser(userExecuted.PartitionKeys.AggregateId));
        Assert.NotNull(confirmResult2);
        Assert.False(confirmResult2.IsSuccess); // already confirmed, it should fail
        Assert.IsType<SekibanAggregateTypeRestrictionException>(confirmResult2.GetException());

        // var revokeResultFail = await executor.CommandAsync(
        //     new RevokeUser(userExecuted.PartitionKeys.AggregateId));
        // Assert.NotNull(revokeResultFail);
        // Assert.False(revokeResultFail.IsSuccess); // when use not exists, it should fail
        // Assert.IsType<ApplicationException>(revokeResultFail.GetException());

        var revokeResult = await executor.CommandAsync(new RevokeUser(userExecuted.PartitionKeys.AggregateId));
        Assert.NotNull(revokeResult);
        Assert.True(revokeResult.IsSuccess);

        var revokeResult2 = await executor.CommandAsync(new RevokeUser(userExecuted.PartitionKeys.AggregateId));
        Assert.NotNull(revokeResult2);
        Assert.False(revokeResult2.IsSuccess); // already revoked, it should fail
        Assert.IsType<SekibanAggregateTypeRestrictionException>(revokeResult2.GetException());
    }

    // [Fact]
    // public async Task SimpleEventSourcingFunction()
    // {
    //     Repository.Events.Clear();
    //     var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };
    //
    //     Assert.Empty(Repository.Events);
    //     var registerBranch = new RegisterBranch("branch1");
    //     await executor.ExecuteFunction(
    //         registerBranch,
    //         new BranchProjector(),
    //         registerBranch.SpecifyPartitionKeys,
    //         registerBranch.Handle,
    //         CommandMetadata.Create("test"));
    //
    //     Assert.Single(Repository.Events);
    //     var last = Repository.Events.Last();
    //     Assert.IsType<Event<BranchCreated>>(last);
    //
    //     var aggregate = Repository.Load(last.PartitionKeys, new BranchProjector()).UnwrapBox();
    //     var payload = aggregate.GetPayload();
    //     Assert.IsType<Branch>(payload);
    //
    //     var registerUser = new RegisterUser("tomo", "tomo@example.com");
    //     var userExecuted = await executor
    //         .ExecuteFunction(
    //             registerUser,
    //             new UserProjector(),
    //             registerUser.SpecifyPartitionKeys,
    //             new RegisterUser.Injection(_ => false),
    //             registerUser.Handle,
    //             CommandMetadata.Create("test"))
    //         .UnwrapBox();
    //     Assert.NotNull(userExecuted);
    //
    //     var confirmUser = new ConfirmUser(userExecuted.PartitionKeys.AggregateId);
    //
    //     var confirmResult = await executor.ExecuteFunction(
    //         confirmUser,
    //         new UserProjector(),
    //         confirmUser.SpecifyPartitionKeys,
    //         confirmUser.Handle,
    //         CommandMetadata.Create("test"));
    //     Assert.NotNull(confirmResult);
    //     Assert.True(confirmResult.IsSuccess);
    //     var confirmResult2 = await executor.ExecuteFunction(
    //         confirmUser,
    //         new UserProjector(),
    //         confirmUser.SpecifyPartitionKeys,
    //         confirmUser.Handle,
    //         CommandMetadata.Create("test"));
    //     Assert.NotNull(confirmResult2);
    //     Assert.False(confirmResult2.IsSuccess); // already confirmed, it should fail
    //     Assert.IsType<SekibanAggregateTypeRestrictionException>(confirmResult2.GetException());
    //
    //     var revokeCommand = new RevokeUser(userExecuted.PartitionKeys.AggregateId);
    //     var revokeResultFail = await executor.ExecuteFunction(
    //         revokeCommand,
    //         new UserProjector(),
    //         revokeCommand.SpecifyPartitionKeys,
    //         new RevokeUser.Injection(_ => false),
    //         revokeCommand.Handle,
    //         CommandMetadata.Create("test"));
    //     Assert.NotNull(revokeResultFail);
    //     Assert.False(revokeResultFail.IsSuccess); // when use not exists, it should fail
    //     Assert.IsType<ApplicationException>(revokeResultFail.GetException());
    //
    //     var revokeResult = await executor.ExecuteFunction(
    //         revokeCommand,
    //         new UserProjector(),
    //         revokeCommand.SpecifyPartitionKeys,
    //         new RevokeUser.Injection(_ => true),
    //         revokeCommand.Handle,
    //         CommandMetadata.Create("test"));
    //     Assert.NotNull(revokeResult);
    //     Assert.True(revokeResult.IsSuccess);
    //
    //     var revokeResult2 = await executor.ExecuteFunction(
    //         revokeCommand,
    //         new UserProjector(),
    //         revokeCommand.SpecifyPartitionKeys,
    //         new RevokeUser.Injection(_ => true),
    //         revokeCommand.Handle,
    //         CommandMetadata.Create("test"));
    //     Assert.NotNull(revokeResult2);
    //     Assert.False(revokeResult2.IsSuccess); // already revoked, it should fail
    //     Assert.IsType<SekibanAggregateTypeRestrictionException>(revokeResult2.GetException());
    // }

    // [Fact]
    // public async Task SimpleEventSourcingResource()
    // {
    //     Repository.Events.Clear();
    //     var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };
    //
    //     Assert.Empty(Repository.Events);
    //     var registerBranch = new RegisterBranch("branch1");
    //     await executor.ExecuteWithResource(
    //         registerBranch,
    //         new CommandResource<RegisterBranch, BranchProjector>(
    //             registerBranch.SpecifyPartitionKeys,
    //             registerBranch.Handle),
    //         CommandMetadata.Create("test"));
    //
    //     Assert.Single(Repository.Events);
    //     var last = Repository.Events.Last();
    //     Assert.IsType<Event<BranchCreated>>(last);
    //
    //     var aggregate = Repository.Load(last.PartitionKeys, new BranchProjector()).UnwrapBox();
    //     var payload = aggregate.GetPayload();
    //     Assert.IsType<Branch>(payload);
    //
    //     var registerUser = new RegisterUser("tomo", "tomo@example.com");
    //     var userExecuted = await executor
    //         .ExecuteWithResource(
    //             registerUser,
    //             new CommandResourceWithInject<RegisterUser, UserProjector, RegisterUser.Injection>(
    //                 registerUser.SpecifyPartitionKeys,
    //                 new RegisterUser.Injection(_ => false),
    //                 registerUser.Handle),
    //             CommandMetadata.Create("test"))
    //         .UnwrapBox();
    //
    //     Assert.NotNull(userExecuted);
    //
    //     var confirmUser = new ConfirmUser(userExecuted.PartitionKeys.AggregateId);
    //
    //     var confirmResult = await executor.ExecuteFunction(
    //         confirmUser,
    //         new UserProjector(),
    //         confirmUser.SpecifyPartitionKeys,
    //         confirmUser.Handle,
    //         CommandMetadata.Create("test"));
    //     Assert.NotNull(confirmResult);
    //     Assert.True(confirmResult.IsSuccess);
    //     var confirmResult2 = await executor.ExecuteWithResource(
    //         confirmUser,
    //         new CommandResource<ConfirmUser, UserProjector, UnconfirmedUser>(
    //             confirmUser.SpecifyPartitionKeys,
    //             confirmUser.Handle),
    //         CommandMetadata.Create("test"));
    //     Assert.NotNull(confirmResult2);
    //     Assert.False(confirmResult2.IsSuccess); // already confirmed, it should fail
    //     Assert.IsType<SekibanAggregateTypeRestrictionException>(confirmResult2.GetException());
    //
    //     var revokeCommand = new RevokeUser(userExecuted.PartitionKeys.AggregateId);
    //     var revokeResultFailggg = await executor.ExecuteFunction(
    //         revokeCommand,
    //         new UserProjector(),
    //         revokeCommand.SpecifyPartitionKeys,
    //         new RevokeUser.Injection(_ => false),
    //         revokeCommand.Handle,
    //         CommandMetadata.Create("test"));
    //     var revokeResultFail = await executor.ExecuteWithResource(
    //         revokeCommand,
    //         new CommandResourceWithInject<RevokeUser, UserProjector, ConfirmedUser, RevokeUser.Injection>(
    //             revokeCommand.SpecifyPartitionKeys,
    //             new RevokeUser.Injection(_ => false),
    //             revokeCommand.Handle),
    //         CommandMetadata.Create("test"));
    //     Assert.NotNull(revokeResultFail);
    //     Assert.False(revokeResultFail.IsSuccess); // when use not exists, it should fail
    //     Assert.IsType<ApplicationException>(revokeResultFail.GetException());
    //
    //     var revokeResult = await executor.ExecuteWithResource(
    //         revokeCommand,
    //         new CommandResourceWithInject<RevokeUser, UserProjector, ConfirmedUser, RevokeUser.Injection>(
    //             revokeCommand.SpecifyPartitionKeys,
    //             new RevokeUser.Injection(_ => true),
    //             revokeCommand.Handle),
    //         CommandMetadata.Create("test"));
    //     Assert.NotNull(revokeResult);
    //     Assert.True(revokeResult.IsSuccess);
    //     var revokeResult2 = await executor.ExecuteWithResource(
    //         revokeCommand,
    //         new CommandResourceWithInject<RevokeUser, UserProjector, ConfirmedUser, RevokeUser.Injection>(
    //             revokeCommand.SpecifyPartitionKeys,
    //             new RevokeUser.Injection(_ => true),
    //             revokeCommand.Handle),
    //         CommandMetadata.Create("test"));
    //     Assert.NotNull(revokeResult2);
    //     Assert.False(revokeResult2.IsSuccess); // already revoked, it should fail
    //     Assert.IsType<SekibanAggregateTypeRestrictionException>(revokeResult2.GetException());
    // }
}
