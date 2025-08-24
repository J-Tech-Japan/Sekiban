using Microsoft.Extensions.DependencyInjection;
using Pure.Domain.Generated;
using ResultBoxes;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Repositories;
namespace Pure.Domain.Test;

public class EventOrNoneVersionTest
{
    [Fact]
    public async Task EventOrNone_None_Should_Not_Increment_Version()
    {
        // Arrange
        InMemorySekibanExecutor executor = new(
            PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options),
            new FunctionCommandMetadataProvider(() => "test"),
            new Repository(),
            new ServiceCollection().BuildServiceProvider());

        // Create and confirm a user
        var registerResult = await executor.CommandAsync(new RegisterUser("John", "john@example.com")).UnwrapBox();
        var userId = registerResult.PartitionKeys.AggregateId;

        // Confirm the user
        await executor.CommandAsync(new ConfirmUser(userId)).UnwrapBox();

        // Get initial aggregate state
        var initialAggregate = executor.Repository.Load(registerResult.PartitionKeys, new UserProjector()).UnwrapBox();

        Assert.Equal(2, initialAggregate.Version); // Version 2 after register and confirm

        // Act: Try to update with the same name (should return EventOrNone.None)
        var updateResult = await executor.CommandAsync(new UpdateUserName(userId, "John"));

        // Assert: Version should not have changed
        Assert.True(updateResult.IsSuccess);

        var aggregateAfterNoOp
            = executor.Repository.Load(registerResult.PartitionKeys, new UserProjector()).UnwrapBox();

        Assert.Equal(2, aggregateAfterNoOp.Version); // Version should remain 2
        Assert.Equal("John", ((ConfirmedUser)aggregateAfterNoOp.GetPayload()).Name);
    }

    [Fact]
    public async Task EventOrNone_WithEvent_Should_Increment_Version()
    {
        // Arrange
        InMemorySekibanExecutor executor = new(
            PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options),
            new FunctionCommandMetadataProvider(() => "test"),
            new Repository(),
            new ServiceCollection().BuildServiceProvider());

        // Create and confirm a user
        var registerResult = await executor.CommandAsync(new RegisterUser("John", "john@example.com")).UnwrapBox();
        var userId = registerResult.PartitionKeys.AggregateId;

        // Confirm the user
        await executor.CommandAsync(new ConfirmUser(userId)).UnwrapBox();

        // Act: Update with a different name (should produce an event)
        var updateResult = await executor.CommandAsync(new UpdateUserName(userId, "Jane")).UnwrapBox();

        // Assert: Version should have incremented
        var aggregateAfterUpdate
            = executor.Repository.Load(registerResult.PartitionKeys, new UserProjector()).UnwrapBox();

        Assert.Equal(3, aggregateAfterUpdate.Version); // Version should be 3
        Assert.Equal("Jane", ((ConfirmedUser)aggregateAfterUpdate.GetPayload()).Name);
    }

    [Fact]
    public async Task Multiple_EventOrNone_None_Should_Not_Change_Version()
    {
        // Arrange
        InMemorySekibanExecutor executor = new(
            PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options),
            new FunctionCommandMetadataProvider(() => "test"),
            new Repository(),
            new ServiceCollection().BuildServiceProvider());

        // Create and confirm a user
        var registerResult = await executor.CommandAsync(new RegisterUser("John", "john@example.com")).UnwrapBox();
        var userId = registerResult.PartitionKeys.AggregateId;

        // Confirm the user
        await executor.CommandAsync(new ConfirmUser(userId)).UnwrapBox();

        // Act: Multiple updates with the same name
        await executor.CommandAsync(new UpdateUserName(userId, "John"));
        await executor.CommandAsync(new UpdateUserName(userId, "John"));
        await executor.CommandAsync(new UpdateUserName(userId, "John"));

        // Assert: Version should still be 2
        var aggregate = executor.Repository.Load(registerResult.PartitionKeys, new UserProjector()).UnwrapBox();

        Assert.Equal(2, aggregate.Version); // Version should remain 2
    }
}
