using Pure.Domain.Generated;
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
        var executor = new CommandExecutor { EventTypes = new DomainEventTypes() };

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
    }
    [Fact]
    public async Task ChangeBranchNameSpec()
    {
        Repository.Events.Clear();
        var executor = new CommandExecutor { EventTypes = new DomainEventTypes() };

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
        var executor = new CommandExecutor { EventTypes = new DomainEventTypes() };

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
}
