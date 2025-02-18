using Microsoft.Extensions.DependencyInjection;
using Pure.Domain.Generated;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Repositories;
namespace Pure.Domain.Test;

public class CommandExecutionTests
{
    [Fact]
    public async Task ExecuteWithGeneric()
    {
        var executor = new InMemorySekibanExecutor(
            PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options),
            new FunctionCommandMetadataProvider(() => "test"),
            new Repository(),
            new ServiceCollection().BuildServiceProvider());
        var createCommand = new RegisterBranch("a");
        var result = await executor.CommandAsync(createCommand);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ExecuteWithoutGeneric()
    {
        var executor = new CommandExecutor(new ServiceCollection().BuildServiceProvider())
            { EventTypes = new PureDomainEventTypes() };
        var createCommand = new RegisterBranch("a");
        var result = await executor.Execute(createCommand, CommandMetadata.Create("test"));
        Assert.True(result.IsSuccess);
    }

    // [Fact]
    // public async Task ExecuteWithResource1()
    // {
    //     var executor = new InMemorySekibanExecutor(
    //         PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options),
    //         new FunctionCommandMetadataProvider(() => "test"),
    //         new Repository());
    //     var createCommand = new RegisterBranch("a");
    //     var partitionKeys = PartitionKeys.Generate();
    //     var result = await executor.ExecuteWithResource(
    //         createCommand,
    //         new CommandResource<RegisterBranch, BranchProjector>(
    //             command => partitionKeys,
    //             (command, _) => EventOrNone.Event(new BranchCreated(command.Name))),
    //         CommandMetadata.Create("test"));
    //     Assert.True(result.IsSuccess);
    //     var aggregate = Repository.Load<BranchProjector>(partitionKeys);
    //     Assert.NotNull(aggregate);
    //     Assert.IsType<Branch>(aggregate.GetValue().GetPayload());
    //     var branch = aggregate.GetValue().GetPayload() as Branch ?? throw new ApplicationException();
    //     Assert.Equal("a", branch.Name);
    //     Assert.Equal(1, aggregate.GetValue().Version);
    // }
    //
    // [Fact]
    // public async Task ExecuteWithResourcePublishOnly()
    // {
    //     var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };
    //     var createCommand = new RegisterBranch("a");
    //     var partitionKeys = PartitionKeys.Generate();
    //     var result = await executor.ExecuteWithResource(
    //         createCommand,
    //         new CommandResourcePublishOnly<RegisterBranch>(
    //             command => partitionKeys,
    //             (command, _) => EventOrNone.Event(new BranchCreated(command.Name))),
    //         CommandMetadata.Create("test"));
    //     Assert.True(result.IsSuccess);
    //     var aggregate = Repository.Load<BranchProjector>(partitionKeys);
    //     Assert.NotNull(aggregate);
    //     Assert.IsType<Branch>(aggregate.GetValue().GetPayload());
    //     var branch = aggregate.GetValue().GetPayload() as Branch ?? throw new ApplicationException();
    //     Assert.Equal("a", branch.Name);
    //     Assert.Equal(1, aggregate.GetValue().Version);
    // }

    // [Fact]
    // public async Task ExecuteWithResourcePublishOnlyTask()
    // {
    //     var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };
    //     var createCommand = new RegisterBranch("a");
    //     var partitionKeys = PartitionKeys.Generate();
    //     var result = await executor.ExecuteWithResource(
    //         createCommand,
    //         new CommandResourcePublishOnlyTask<RegisterBranch>(
    //             command => partitionKeys,
    //             (command, _) => Task.FromResult(EventOrNone.Event(new BranchCreated(command.Name)))),
    //         CommandMetadata.Create("test"));
    //     Assert.True(result.IsSuccess);
    //     var aggregate = Repository.Load<BranchProjector>(partitionKeys);
    //     Assert.NotNull(aggregate);
    //     Assert.IsType<Branch>(aggregate.GetValue().GetPayload());
    //     var branch = aggregate.GetValue().GetPayload() as Branch ?? throw new ApplicationException();
    //     Assert.Equal("a", branch.Name);
    //     Assert.Equal(1, aggregate.GetValue().Version);
    // }

    // [Fact]
    // public async Task ExecuteWithResourcePublishOnlyInject()
    // {
    //     var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };
    //     var createCommand = new RegisterBranch("a");
    //     var partitionKeys = PartitionKeys.Generate();
    //     var result = await executor.ExecuteWithResource(
    //         createCommand,
    //         new CommandResourcePublishOnlyWithInject<RegisterBranch, Func<string, bool>>(
    //             command => partitionKeys,
    //             _ => false,
    //             (command, _, _) => EventOrNone.Event(new BranchCreated(command.Name))),
    //         CommandMetadata.Create("test"));
    //     Assert.True(result.IsSuccess);
    //     var aggregate = Repository.Load<BranchProjector>(partitionKeys);
    //     Assert.NotNull(aggregate);
    //     Assert.IsType<Branch>(aggregate.GetValue().GetPayload());
    //     var branch = aggregate.GetValue().GetPayload() as Branch ?? throw new ApplicationException();
    //     Assert.Equal("a", branch.Name);
    //     Assert.Equal(1, aggregate.GetValue().Version);
    // }

    // [Fact]
    // public async Task ExecuteWithResourcePublishOnlyWithInjectTask()
    // {
    //     var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };
    //     var createCommand = new RegisterBranch("a");
    //     var partitionKeys = PartitionKeys.Generate();
    //     var result = await executor.ExecuteWithResource(
    //         createCommand,
    //         new CommandResourcePublishOnlyWithInjectTask<RegisterBranch, Func<string, bool>>(
    //             command => partitionKeys,
    //             _ => false,
    //             (command, _, _) => Task.FromResult(EventOrNone.Event(new BranchCreated(command.Name)))),
    //         CommandMetadata.Create("test"));
    //     Assert.True(result.IsSuccess);
    //     var aggregate = Repository.Load<BranchProjector>(partitionKeys);
    //     Assert.NotNull(aggregate);
    //     Assert.IsType<Branch>(aggregate.GetValue().GetPayload());
    //     var branch = aggregate.GetValue().GetPayload() as Branch ?? throw new ApplicationException();
    //     Assert.Equal("a", branch.Name);
    //     Assert.Equal(1, aggregate.GetValue().Version);
    // }
}
