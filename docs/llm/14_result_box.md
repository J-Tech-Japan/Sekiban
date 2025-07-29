# ResultBox - Sekiban Event Sourcing

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
> - [Dapr Setup](11_dapr_setup.md)
> - [Unit Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md) (You are here)
> - [Value Object](15_value_object.md)
> - [Deployment Guide](16_deployment.md)

## Introduction to ResultBox

ResultBox is a powerful utility type from the `ResultBoxes` package that is essential to Sekiban domain and API code. It helps with error handling, method chaining, and ensuring that operations are properly completed or failing gracefully.

## Core Concepts

### What is ResultBox?

ResultBox is a container that wraps a result value along with information about whether an operation was successful. It provides:

1. **Error Handling** - Safely manages errors without throwing exceptions
2. **Method Chaining** - Allows fluent composition of operations
3. **Unwrapping** - Extracts the final value or throws an exception if any step failed

### Basic Usage

ResultBox is used throughout Sekiban to handle operations that might fail, allowing for fluent error handling. Here's a simple example:

```csharp
// A method returning a ResultBox
public ResultBox<User> GetUserById(string id)
{
    if (string.IsNullOrEmpty(id))
    {
        return ResultBox.Error<User>("User ID cannot be empty");
    }
    
    var user = repository.FindUser(id);
    if (user == null)
    {
        return ResultBox.Error<User>($"User with ID {id} not found");
    }
    
    return ResultBox.Ok(user);
}
```

## Method Chaining with ResultBox

One of the most powerful features of ResultBox is its ability to chain operations using extension methods:

### Key Extension Methods

1. **Conveyor** - Transforms the result into a new ResultBox if the operation was successful
2. **Do** - Executes an action on the value if the operation was successful
3. **UnwrapBox** - Extracts the value from the ResultBox or throws an exception if the operation failed

### Method Chaining in API Implementation

ResultBox is commonly used in API endpoints to handle command execution:

```csharp
// Example API endpoint using ResultBox
[HttpPost("createuser")]
public async Task<ActionResult<CommandResponseSimple>> CreateUser(
    [FromBody] CreateUserCommand command,
    [FromServices] SekibanOrleansExecutor executor)
{
    return await executor.CommandAsync(command)
        .ToSimpleCommandResponse()  // Convert to a simpler response format
        .UnwrapBox();  // Unwrap the result or throw an exception
}
```

In this pattern:
1. `CommandAsync` returns a ResultBox with the command response
2. `ToSimpleCommandResponse()` transforms it to a more client-friendly format
3. `UnwrapBox()` extracts the final value or throws an appropriate exception

### Method Chaining in Testing

ResultBox is particularly useful in unit tests for creating fluent test chains:

```csharp
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
```

In this test:
1. Each operation is chained to the next using `Conveyor`
2. Assertions are made using `Do` without breaking the chain
3. `UnwrapBox` at the end ensures any failure in the chain throws an exception

## Error Handling with ResultBox

ResultBox can handle errors elegantly without throwing exceptions until you're ready to deal with them:

```csharp
public async Task<ResultBox<CommandResponseSimple>> ExecuteCommand(CreateItemCommand command)
{
    try
    {
        // Execute the command
        var result = await executor.CommandAsync(command);
        
        // Check if the command succeeded before continuing
        if (!result.IsSuccess)
        {
            return ResultBox.Error<CommandResponseSimple>(result.ErrorMessage);
        }
        
        // Convert to a simpler response
        return result.ToSimpleCommandResponse();
    }
    catch (Exception ex)
    {
        // Wrap exceptions in a ResultBox
        return ResultBox.Error<CommandResponseSimple>(ex.Message);
    }
}
```

## Best Practices

1. **Prefer Method Chains** - Use method chaining rather than unwrapping prematurely to maintain the error context
2. **Unwrap at Boundaries** - Only call `UnwrapBox()` at application boundaries like API controllers
3. **Meaningful Error Messages** - Provide clear error messages when creating error ResultBoxes
4. **Transform with Conveyor** - Use `Conveyor` to transform values while maintaining the success/failure state
5. **Side Effects with Do** - Use `Do` for assertions or logging without breaking the chain

## Conclusion

ResultBox is a fundamental part of Sekiban that enables fluent error handling, elegant method chaining, and clean API design. By understanding and properly using ResultBox, you can write more robust, readable, and maintainable code in your Sekiban applications.