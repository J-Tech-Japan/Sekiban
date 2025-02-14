using Pure.Domain.Generated;
using ResultBoxes;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Events;
namespace Pure.Domain.Test;

public class BranchManagementTests
{
    [Fact]
    public async Task ChangeBranchNameSpec()
    {
        var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };

        Assert.Empty(executor.Repository.Events);
        var executed = await executor.Execute(new RegisterBranch("branch1"), CommandMetadata.Create("test"));
        Assert.True(executed.IsSuccess);
        var aggregateId = executed.GetValue().PartitionKeys.AggregateId;

        Assert.Single(executor.Repository.Events);
        var last = executor.Repository.Events.Last();
        Assert.IsType<Event<BranchCreated>>(last);

        await executor.Execute(new ChangeBranchName(aggregateId, "branch name2"), CommandMetadata.Create("test"));

        Assert.Equal(2, executor.Repository.Events.Count);
        last = executor.Repository.Events.Last();
        Assert.IsType<Event<BranchNameChanged>>(last);

        var aggregate = executor.Repository.Load(last.PartitionKeys, new BranchProjector()).UnwrapBox();
        var payload = aggregate.GetPayload() as Branch;
        Assert.NotNull(payload);
        Assert.Equal("branch name2", payload.Name);
    }

    [Fact]
    public async Task MultipleBranchesSpec()
    {
        var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };

        Assert.Empty(executor.Repository.Events);
        var executed = await executor.Execute(new RegisterBranch("branch 0"), CommandMetadata.Create("test"));
        executed = await executor.Execute(new RegisterBranch("branch1"), CommandMetadata.Create("test"));
        Assert.True(executed.IsSuccess);
        var aggregateId = executed.GetValue().PartitionKeys.AggregateId;

        Assert.Equal(2, executor.Repository.Events.Count);
        var last = executor.Repository.Events.Last();
        Assert.IsType<Event<BranchCreated>>(last);

        await executor.Execute(new ChangeBranchName(aggregateId, "branch name2"), CommandMetadata.Create("test"));

        Assert.Equal(3, executor.Repository.Events.Count);
        last = executor.Repository.Events.Last();
        Assert.IsType<Event<BranchNameChanged>>(last);
        Assert.Equal(2, last.Version);

        var aggregate = executor.Repository.Load(last.PartitionKeys, new BranchProjector()).UnwrapBox();
        var payload = aggregate.GetPayload() as Branch;
        Assert.NotNull(payload);
        Assert.Equal("branch name2", payload.Name);
        Assert.Equal(2, aggregate.Version);
    }

    [Fact]
    public void CanUseDelegateSpec()
    {
        var confirmUser = new ConfirmUser(Guid.CreateVersion7());
        Delegate d = confirmUser.Handle;
        Assert.IsType<Func<ConfirmUser, ICommandContext<UnconfirmedUser>, ResultBox<EventOrNone>>>(d);
    }

    // [Fact]
    // public async Task ICommandAndICommandWithAggregateRestrictionShouldWorkWithFunctionTest()
    // {
    //     var repository = new Repository();
    //     var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };
    //
    //     var command1 = new RegisterBranch2("aaa");
    //     var result = await executor.ExecuteFunction(
    //         command1,
    //         new BranchProjector(),
    //         branch2 => PartitionKeys.Generate(),
    //         (branch2, context) => EventOrNone.Event(new BranchCreated(branch2.Name)),
    //         CommandMetadata.Create("test"));
    //     Assert.True(result.IsSuccess);
    //     var aggregate = Repository.Load<BranchProjector>(result.GetValue().PartitionKeys);
    //     Assert.NotNull(aggregate);
    //     Assert.IsType<Branch>(aggregate.GetValue().GetPayload());
    //     var branch = aggregate.GetValue().GetPayload() as Branch ?? throw new ApplicationException();
    //     Assert.Equal("aaa", branch.Name);
    //
    //     var command2 = new RegisterBranch3("bbb");
    //     var result2 = await executor.ExecuteFunction(
    //         command2,
    //         new BranchProjector(),
    //         branch2 => PartitionKeys.Generate(),
    //         (branch3, context) => EventOrNone.Event(new BranchCreated(branch3.Name)),
    //         CommandMetadata.Create("test"));
    //     Assert.True(result2.IsSuccess);
    //     var aggregate2 = repository.Load<BranchProjector>(result2.GetValue().PartitionKeys);
    //     Assert.NotNull(aggregate2);
    //     Assert.IsType<Branch>(aggregate2.GetValue().GetPayload());
    //     var branch2 = aggregate2.GetValue().GetPayload() as Branch ?? throw new ApplicationException();
    //     Assert.Equal("bbb", branch2.Name);
    // }
}
