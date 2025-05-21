# Unit Testing - Sekiban Event Sourcing

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Aggregate, Projector, Command and Events](03_aggregate_command_events.md)
> - [Multiple Aggregate Projector](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Workflow](06_workflow.md)
> - [JSON and Orleans Serialization](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md)
> - [Client API (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md)
> - [Unit Testing](11_unit_testing.md) (You are here)
> - [Common Issues and Solutions](12_common_issues.md)

## Unit Testing

Sekiban provides several approaches for unit testing your event-sourced applications. You can choose between in-memory testing for simplicity or Orleans-based testing for more complex scenarios.

## Setup Testing Project

First, create a testing project and add the necessary NuGet packages:

```bash
dotnet new xunit -n YourProject.Tests
dotnet add package Sekiban.Testing
```

## 1. In-Memory Testing with SekibanInMemoryTestBase

The simplest approach uses the `SekibanInMemoryTestBase` class from the `Sekiban.Pure.xUnit` namespace:

```csharp
using Sekiban.Pure;
using Sekiban.Pure.xUnit;
using System;
using Xunit;
using YourProject.Domain;
using YourProject.Domain.Aggregates.YourEntity.Commands;
using YourProject.Domain.Aggregates.YourEntity.Payloads;
using YourProject.Domain.Generated;

public class YourTests : SekibanInMemoryTestBase
{
    // Override to provide your domain types
    protected override SekibanDomainTypes GetDomainTypes() => 
        YourDomainTypes.Generate(YourEventsJsonContext.Default.Options);

    [Fact]
    public void SimpleTest()
    {
        // Given - Execute a command and get the response
        var response1 = GivenCommand(new CreateYourEntity("Name", "Value"));
        Assert.Equal(1, response1.Version);

        // When - Execute another command on the same aggregate
        var response2 = WhenCommand(new UpdateYourEntity(response1.PartitionKeys.AggregateId, "NewValue"));
        Assert.Equal(2, response2.Version);

        // Then - Get the aggregate and verify its state
        var aggregate = ThenGetAggregate<YourEntityProjector>(response2.PartitionKeys);
        var entity = (YourEntity)aggregate.Payload;
        Assert.Equal("NewValue", entity.Value);
        
        // Then - Execute a query and verify the result
        var queryResult = ThenQuery(new YourEntityExistsQuery("Name"));
        Assert.True(queryResult);
    }
}
```

## 2. Method Chaining with ResultBox

For more fluent tests, you can use the ResultBox-based methods that support method chaining:

```csharp
public class YourTests : SekibanInMemoryTestBase
{
    protected override SekibanDomainTypes GetDomainTypes() => 
        YourDomainTypes.Generate(YourEventsJsonContext.Default.Options);

    [Fact]
    public void ChainedTest()
        => GivenCommandWithResult(new CreateYourEntity("Name", "Value"))
            .Do(response => Assert.Equal(1, response.Version))
            .Conveyor(response => WhenCommandWithResult(new UpdateYourEntity(response.PartitionKeys.AggregateId, "NewValue")))
            .Do(response => Assert.Equal(2, response.Version))
            .Conveyor(response => ThenGetAggregateWithResult<YourEntityProjector>(response.PartitionKeys))
            .Conveyor(aggregate => aggregate.Payload.ToResultBox().Cast<YourEntity>())
            .Do(payload => Assert.Equal("NewValue", payload.Value))
            .Conveyor(_ => ThenQueryWithResult(new YourEntityExistsQuery("Name")))
            .Do(Assert.True)
            .UnwrapBox();
}
```

Key points:
- `Conveyor` is used to chain operations, transforming the result of one operation into the input for the next
- `Do` is used to perform assertions or side effects without changing the result
- `UnwrapBox` at the end unwraps the final ResultBox, throwing an exception if any step failed

## 3. Orleans Testing with SekibanOrleansTestBase

For testing with Orleans integration, use the `SekibanOrleansTestBase` class from the `Sekiban.Pure.Orleans.xUnit` namespace:

```csharp
public class YourOrleansTests : SekibanOrleansTestBase<YourOrleansTests>
{
    public override SekibanDomainTypes GetDomainTypes() => 
        YourDomainTypes.Generate(YourEventsJsonContext.Default.Options);

    [Fact]
    public void OrleansTest() =>
        GivenCommandWithResult(new CreateYourEntity("Name", "Value"))
            .Do(response => Assert.Equal(1, response.Version))
            .Conveyor(response => WhenCommandWithResult(new UpdateYourEntity(response.PartitionKeys.AggregateId, "NewValue")))
            .Do(response => Assert.Equal(2, response.Version))
            .Conveyor(response => ThenGetAggregateWithResult<YourEntityProjector>(response.PartitionKeys))
            .Conveyor(aggregate => aggregate.Payload.ToResultBox().Cast<YourEntity>())
            .Do(payload => Assert.Equal("NewValue", payload.Value))
            .Conveyor(_ => ThenGetMultiProjectorWithResult<AggregateListProjector<YourEntityProjector>>())
            .Do(projector => 
            {
                Assert.Equal(1, projector.Aggregates.Values.Count());
                var entity = (YourEntity)projector.Aggregates.Values.First().Payload;
                Assert.Equal("NewValue", entity.Value);
            })
            .UnwrapBox();
            
    [Fact]
    public void TestSerializable()
    {
        // Test that commands are serializable (important for Orleans)
        CheckSerializability(new CreateYourEntity("Name", "Value"));
    }
}
```

## 4. Manual Testing with InMemorySekibanExecutor

For more complex scenarios or custom test setups, you can manually create an `InMemorySekibanExecutor`:

```csharp
[Fact]
public async Task ManualExecutorTest()
{
    // Create an in-memory executor
    var executor = new InMemorySekibanExecutor(
        YourDomainTypes.Generate(YourEventsJsonContext.Default.Options),
        new FunctionCommandMetadataProvider(() => "test"),
        new Repository(),
        new ServiceCollection().BuildServiceProvider());

    // Execute a command
    var result = await executor.CommandAsync(new CreateYourEntity("Name", "Value"));
    Assert.True(result.IsSuccess);
    var value = result.GetValue();
    Assert.NotNull(value);
    Assert.Equal(1, value.Version);
    var aggregateId = value.PartitionKeys.AggregateId;

    // Load the aggregate
    var aggregateResult = await executor.LoadAggregateAsync<YourEntityProjector>(
        PartitionKeys.Existing<YourEntityProjector>(aggregateId));
    Assert.True(aggregateResult.IsSuccess);
    var aggregate = aggregateResult.GetValue();
    var entity = (YourEntity)aggregate.Payload;
    Assert.Equal("Name", entity.Name);
    Assert.Equal("Value", entity.Value);
}
```

## Testing Workflows

```csharp
public class DuplicateCheckWorkflowsTests : SekibanInMemoryTestBase
{
    protected override SekibanDomainTypes GetDomainTypes() => 
        YourDomainDomainTypes.Generate(YourDomainEventsJsonContext.Default.Options);

    [Fact]
    public async Task CheckUserIdDuplicate_WhenUserIdExists_ReturnsDuplicate()
    {
        // Arrange - Create a user with the ID we want to test
        var existingUserId = "U12345";
        var command = new RegisterUserCommand(
            "John Doe",
            existingUserId,
            "john@example.com");

        // Register a user with the same ID to ensure it exists
        GivenCommand(command);

        // Act - Try to register another user with the same ID
        var result = await DuplicateCheckWorkflows.CheckUserIdDuplicate(command, Executor);

        // Assert
        Assert.True(result.IsDuplicate);
        Assert.Contains(existingUserId, result.ErrorMessage);
        Assert.Null(result.CommandResult);
    }

    [Fact]
    public async Task CheckUserIdDuplicate_WhenUserIdDoesNotExist_ReturnsSuccess()
    {
        // Arrange
        var newUserId = "U67890";
        var command = new RegisterUserCommand(
            "Jane Doe",
            newUserId,
            "jane@example.com");

        // Act
        var result = await DuplicateCheckWorkflows.CheckUserIdDuplicate(command, Executor);

        // Assert
        Assert.False(result.IsDuplicate);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.CommandResult);
    }
}
```

## Testing with Given-When-Then Pattern

Sekiban's testing tools support the Given-When-Then pattern for more expressive tests:

```csharp
[Fact]
public void UserRegistrationAndConfirmation()
{
    // Given - A registered user
    var registeredUserResponse = GivenCommand(new RegisterUserCommand(
        "John Doe", 
        "john@example.com", 
        "Password123"));
    
    var userId = registeredUserResponse.PartitionKeys.AggregateId;
    
    // Then - User should be in unconfirmed state
    var unconfirmedAggregate = ThenGetAggregate<UserProjector>(registeredUserResponse.PartitionKeys);
    Assert.IsType<UnconfirmedUser>(unconfirmedAggregate.Payload);
    var unconfirmedUser = (UnconfirmedUser)unconfirmedAggregate.Payload;
    Assert.Equal("John Doe", unconfirmedUser.Name);
    Assert.Equal("john@example.com", unconfirmedUser.Email);
    
    // When - Confirm the user
    var confirmationResponse = WhenCommand(new ConfirmUserCommand(userId));
    
    // Then - User should be in confirmed state
    var confirmedAggregate = ThenGetAggregate<UserProjector>(confirmationResponse.PartitionKeys);
    Assert.IsType<ConfirmedUser>(confirmedAggregate.Payload);
    var confirmedUser = (ConfirmedUser)confirmedAggregate.Payload;
    Assert.Equal("John Doe", confirmedUser.Name);
    Assert.Equal("john@example.com", confirmedUser.Email);
}
```

## Testing Multi-Projectors

```csharp
[Fact]
public void MultiProjectorTest()
{
    // Given - Order placed
    var placeOrderResponse = GivenCommand(new PlaceOrderCommand(
        "customer123",
        new[] { new OrderItemDto("product1", 2, 10.0m) }));
        
    // When - Another order placed
    var placeOrder2Response = WhenCommand(new PlaceOrderCommand(
        "customer123",
        new[] { new OrderItemDto("product2", 1, 15.0m) }));
        
    // Then - OrderStatistics should reflect both orders
    var statistics = ThenGetMultiProjector<OrderStatisticsProjector>();
    
    Assert.Equal(2, statistics.TotalOrders);
    Assert.Equal(35.0m, statistics.TotalRevenue);
    
    // Check customer stats
    Assert.True(statistics.CustomerStatistics.TryGetValue("customer123", out var customerStats));
    Assert.Equal(2, customerStats.OrderCount);
    Assert.Equal(35.0m, customerStats.TotalSpent);
    
    // Check product sales
    Assert.Equal(2, statistics.ProductSales["product1"]);
    Assert.Equal(1, statistics.ProductSales["product2"]);
}
```

## Best Practices

1. **Test Commands**: Verify that commands produce the expected events and state changes
2. **Test Projectors**: Verify that projectors correctly apply events to build the aggregate state
3. **Test Queries**: Verify that queries return the expected results based on the current state
4. **Test State Transitions**: Verify that state transitions work correctly, especially when using different payload types
5. **Test Error Cases**: Verify that commands fail appropriately when validation fails
6. **Test Serialization**: For Orleans tests, verify that commands and events are serializable
7. **Use GivenCommand for Setup**: Use `GivenCommand` to set up the test state
8. **Use WhenCommand for Actions**: Use `WhenCommand` for the action being tested
9. **Use ThenGetAggregate and ThenQuery for Assertions**: Use these methods for verification
10. **Keep Tests Focused**: Each test should focus on a single behavior
