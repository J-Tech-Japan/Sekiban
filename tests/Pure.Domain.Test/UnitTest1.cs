﻿using Pure.Domain.Generated;
using ResultBoxes;
using Sekiban.Pure;
using Sekiban.Pure.Exception;
namespace Pure.Domain.Test;

public class UnitTest1
{
    [Fact]
    public void Test1()
    {
        var version = GetVersion<UserProjector>();
        Assert.Equal("1.0.1", version);
    }
    [Fact]
    public void PartitionKeysTest()
    {
        var partitionKeys = PartitionKeys.Generate();
        Assert.Equal(PartitionKeys.DefaultAggregateGroupName, partitionKeys.Group);
        Assert.Equal(PartitionKeys.DefaultRootPartitionKey, partitionKeys.RootPartitionKey);
    }
    [Fact]
    public void TenantPartitionKeysTest()
    {
        var partitionKeys = TenantPartitionKeys.Tenant("tenant1").Generate("group1");
        Assert.Equal("tenant1", partitionKeys.RootPartitionKey);
        Assert.Equal("group1", partitionKeys.Group);
    }
    public string GetVersion<TAggregateProjector>() where TAggregateProjector : IAggregateProjector, new() =>
        new TAggregateProjector().GetVersion();
    [Fact]
    public async Task SimpleEventSourcing()
    {
        Repository.Events.Clear();
        var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };

        Assert.Empty(Repository.Events);
        await executor.Execute(new RegisterBranch("branch1"));

        Assert.Single(Repository.Events);
        var last = Repository.Events.Last();
        Assert.IsType<Event<BranchCreated>>(last);

        var aggregate = Repository.Load(last.PartitionKeys, new BranchProjector()).UnwrapBox();
        var payload = aggregate.GetPayload();
        Assert.IsType<Branch>(payload);

        var userExecuted = await executor
            .Execute(new RegisterUser("tomo", "tomo@example.com"), new RegisterUser.Injection(_ => false))
            .UnwrapBox();
        Assert.NotNull(userExecuted);
        var confirmResult = await executor.Execute(new ConfirmUser(userExecuted.PartitionKeys.AggregateId));
        Assert.NotNull(confirmResult);
        Assert.True(confirmResult.IsSuccess);
        var confirmResult2 = await executor.Execute(new ConfirmUser(userExecuted.PartitionKeys.AggregateId));
        Assert.NotNull(confirmResult2);
        Assert.False(confirmResult2.IsSuccess); // already confirmed, it should fail
        Assert.IsType<SekibanAggregateTypeRestrictionException>(confirmResult2.GetException());

        var revokeResultFail = await executor.Execute(
            new RevokeUser(userExecuted.PartitionKeys.AggregateId),
            new RevokeUser.Injection(_ => false));
        Assert.NotNull(revokeResultFail);
        Assert.False(revokeResultFail.IsSuccess); // when use not exists, it should fail
        Assert.IsType<ApplicationException>(revokeResultFail.GetException());


        var revokeResult = await executor.Execute(
            new RevokeUser(userExecuted.PartitionKeys.AggregateId),
            new RevokeUser.Injection(_ => true));
        Assert.NotNull(revokeResult);
        Assert.True(revokeResult.IsSuccess);

        var revokeResult2 = await executor.Execute(
            new RevokeUser(userExecuted.PartitionKeys.AggregateId),
            new RevokeUser.Injection(_ => true));
        Assert.NotNull(revokeResult2);
        Assert.False(revokeResult2.IsSuccess); // already revoked, it should fail
        Assert.IsType<SekibanAggregateTypeRestrictionException>(revokeResult2.GetException());

    }
    [Fact]
    public async Task SimpleEventSourcingFunction()
    {
        Repository.Events.Clear();
        var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };

        Assert.Empty(Repository.Events);
        var registerBranch = new RegisterBranch("branch1");
        await executor.ExecuteFunction(
            registerBranch,
            new BranchProjector(),
            registerBranch.SpecifyPartitionKeys,
            registerBranch.Handle);

        Assert.Single(Repository.Events);
        var last = Repository.Events.Last();
        Assert.IsType<Event<BranchCreated>>(last);

        var aggregate = Repository.Load(last.PartitionKeys, new BranchProjector()).UnwrapBox();
        var payload = aggregate.GetPayload();
        Assert.IsType<Branch>(payload);

        var registerUser = new RegisterUser("tomo", "tomo@example.com");
        var userExecuted = await executor
            .ExecuteFunction(
                registerUser,
                new UserProjector(),
                registerUser.SpecifyPartitionKeys,
                new RegisterUser.Injection(_ => false),
                registerUser.Handle)
            .UnwrapBox();
        Assert.NotNull(userExecuted);

        var confirmUser = new ConfirmUser(userExecuted.PartitionKeys.AggregateId);

        var confirmResult = await executor.ExecuteFunction(
            confirmUser,
            new UserProjector(),
            confirmUser.SpecifyPartitionKeys,
            confirmUser.Handle);
        Assert.NotNull(confirmResult);
        Assert.True(confirmResult.IsSuccess);
        var confirmResult2 = await executor.ExecuteFunction(
            confirmUser,
            new UserProjector(),
            confirmUser.SpecifyPartitionKeys,
            confirmUser.Handle);
        Assert.NotNull(confirmResult2);
        Assert.False(confirmResult2.IsSuccess); // already confirmed, it should fail
        Assert.IsType<SekibanAggregateTypeRestrictionException>(confirmResult2.GetException());




        var revokeCommand = new RevokeUser(userExecuted.PartitionKeys.AggregateId);
        var revokeResultFail = await executor.ExecuteFunction(
            revokeCommand,
            new UserProjector(),
            revokeCommand.SpecifyPartitionKeys,
            new RevokeUser.Injection(_ => false),
            revokeCommand.Handle);
        Assert.NotNull(revokeResultFail);
        Assert.False(revokeResultFail.IsSuccess); // when use not exists, it should fail
        Assert.IsType<ApplicationException>(revokeResultFail.GetException());

        var revokeResult = await executor.ExecuteFunction(
            revokeCommand,
            new UserProjector(),
            revokeCommand.SpecifyPartitionKeys,
            new RevokeUser.Injection(_ => true),
            revokeCommand.Handle);
        Assert.NotNull(revokeResult);
        Assert.True(revokeResult.IsSuccess);

        var revokeResult2 = await executor.ExecuteFunction(
            revokeCommand,
            new UserProjector(),
            revokeCommand.SpecifyPartitionKeys,
            new RevokeUser.Injection(_ => true),
            revokeCommand.Handle);
        Assert.NotNull(revokeResult2);
        Assert.False(revokeResult2.IsSuccess); // already revoked, it should fail
        Assert.IsType<SekibanAggregateTypeRestrictionException>(revokeResult2.GetException());
    }

    [Fact]
    public async Task ChangeBranchNameSpec()
    {
        Repository.Events.Clear();
        var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };

        Assert.Empty(Repository.Events);
        var executed = await executor.Execute(new RegisterBranch("branch1"));
        Assert.True(executed.IsSuccess);
        var aggregateId = executed.GetValue().PartitionKeys.AggregateId;

        Assert.Single(Repository.Events);
        var last = Repository.Events.Last();
        Assert.IsType<Event<BranchCreated>>(last);

        await executor.Execute(new ChangeBranchName(aggregateId, "branch name2"));

        Assert.Equal(2, Repository.Events.Count);
        last = Repository.Events.Last();
        Assert.IsType<Event<BranchNameChanged>>(last);

        var aggregate = Repository.Load(last.PartitionKeys, new BranchProjector()).UnwrapBox();
        var payload = aggregate.GetPayload() as Branch;
        Assert.NotNull(payload);
        Assert.Equal("branch name2", payload.Name);

    }
    [Fact]
    public void CanUseDelegateSpec()
    {
        var confirmUser = new ConfirmUser(Guid.CreateVersion7());
        Delegate d = confirmUser.Handle;
        Assert.IsType<Func<ConfirmUser, ICommandContext<UnconfirmedUser>, ResultBox<EventOrNone>>>(d);

    }

    [Fact]
    public async Task MultipleBranchesSpec()
    {
        Repository.Events.Clear();
        var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };

        Assert.Empty(Repository.Events);
        var executed = await executor.Execute(new RegisterBranch("branch 0"));
        executed = await executor.Execute(new RegisterBranch("branch1"));
        Assert.True(executed.IsSuccess);
        var aggregateId = executed.GetValue().PartitionKeys.AggregateId;

        Assert.Equal(2, Repository.Events.Count);
        var last = Repository.Events.Last();
        Assert.IsType<Event<BranchCreated>>(last);

        await executor.Execute(new ChangeBranchName(aggregateId, "branch name2"));

        Assert.Equal(3, Repository.Events.Count);
        last = Repository.Events.Last();
        Assert.IsType<Event<BranchNameChanged>>(last);
        Assert.Equal(2, last.Version);

        var aggregate = Repository.Load(last.PartitionKeys, new BranchProjector()).UnwrapBox();
        var payload = aggregate.GetPayload() as Branch;
        Assert.NotNull(payload);
        Assert.Equal("branch name2", payload.Name);
        Assert.Equal(2, aggregate.Version);

    }
    [Fact]
    public async Task ICommandAndICommandWithAggregateRestrictionShouldWorkWithFunctionTest()
    {
        Repository.Events.Clear();
        var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };

        var command1 = new RegisterBranch2("aaa");
        var result = await executor.ExecuteFunction(
            command1,
            new BranchProjector(),
            branch2 => PartitionKeys.Generate(),
            (branch2, context) => EventOrNone.Event(new BranchCreated(branch2.Name)));
        Assert.True(result.IsSuccess);
        var aggregate = Repository.Load<BranchProjector>(result.GetValue().PartitionKeys);
        Assert.NotNull(aggregate);
        Assert.IsType<Branch>(aggregate.GetValue().GetPayload());
        var branch = aggregate.GetValue().GetPayload() as Branch ?? throw new ApplicationException();
        Assert.Equal("aaa", branch.Name);


        var command2 = new RegisterBranch3("bbb");
        var result2 = await executor.ExecuteFunction(
            command2,
            new BranchProjector(),
            branch2 => PartitionKeys.Generate(),
            (branch3, context) => EventOrNone.Event(new BranchCreated(branch3.Name)));
        Assert.True(result2.IsSuccess);
        var aggregate2 = Repository.Load<BranchProjector>(result2.GetValue().PartitionKeys);
        Assert.NotNull(aggregate2);
        Assert.IsType<Branch>(aggregate2.GetValue().GetPayload());
        var branch2 = aggregate2.GetValue().GetPayload() as Branch ?? throw new ApplicationException();
        Assert.Equal("bbb", branch2.Name);

    }
}
