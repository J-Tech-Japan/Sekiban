# Workflow - Sekiban Event Sourcing

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Aggregate, Projector, Command and Events](03_aggregate_command_events.md)
> - [Multiple Aggregate Projector](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Workflow](06_workflow.md) (You are here)
> - [JSON and Orleans Serialization](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md)
> - [Client API (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md)
> - [Unit Testing](11_unit_testing.md)
> - [Common Issues and Solutions](12_common_issues.md)

## Workflows and Domain Services

Sekiban supports implementing domain workflows and services that encapsulate business logic that spans multiple aggregates or requires specialized processing.

## Domain Workflows

Domain workflows are stateless services that implement business processes that may involve multiple aggregates or complex validation logic. They are particularly useful for:

1. **Cross-Aggregate Operations**: When a business process spans multiple aggregates
2. **Complex Validation**: When validation requires checking against multiple aggregates or external systems
3. **Reusable Business Logic**: When the same logic is used in multiple places

```csharp
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Query;
using Sekiban.Pure.ResultBoxes;
using System;
using System.Threading.Tasks;

// Example of a domain workflow for duplicate checking
namespace YourProject.Domain.Workflows;

public static class DuplicateCheckWorkflows
{
    // Result type for duplicate check operations
    public class DuplicateCheckResult
    {
        public bool IsDuplicate { get; }
        public string? ErrorMessage { get; }
        public object? CommandResult { get; }

        private DuplicateCheckResult(bool isDuplicate, string? errorMessage, object? commandResult)
        {
            IsDuplicate = isDuplicate;
            ErrorMessage = errorMessage;
            CommandResult = commandResult;
        }

        public static DuplicateCheckResult Duplicate(string errorMessage) => 
            new(true, errorMessage, null);

        public static DuplicateCheckResult Success(object commandResult) => 
            new(false, null, commandResult);
    }

    // Workflow method that checks for duplicate IDs before registering
    public static async Task<DuplicateCheckResult> CheckUserIdDuplicate(
        RegisterUserCommand command,
        ISekibanExecutor executor)
    {
        // Check if userId already exists
        var userIdExists = await executor.QueryAsync(new UserIdExistsQuery(command.UserId)).UnwrapBox();
        if (userIdExists)
        {
            return DuplicateCheckResult.Duplicate($"User with ID '{command.UserId}' already exists");
        }
        
        // If no duplicate, proceed with the command
        var result = await executor.CommandAsync(command).UnwrapBox();
        return DuplicateCheckResult.Success(result);
    }
}
```

**Key Points**:
- Workflows are typically implemented as static classes with static methods
- They should be placed in a `Workflows` folder or namespace
- They should use `ISekibanExecutor` interface for better testability
- They should return domain-specific result types that encapsulate success/failure information
- They can be called from API endpoints or other services

## Using Workflows in API Endpoints

```csharp
// In Program.cs
apiRoute.MapPost("/users/register",
    async ([FromBody] RegisterUserCommand command, [FromServices] SekibanOrleansExecutor executor) => 
    {
        // Use the workflow to check for duplicates
        var result = await DuplicateCheckWorkflows.CheckUserIdDuplicate(command, executor);
        if (result.IsDuplicate)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Duplicate UserId",
                detail: result.ErrorMessage);
        }
        return Results.Ok(result.CommandResult);
    });
```

## Example: Order Processing Workflow

Let's create a more complex workflow for processing orders that involves multiple aggregates and validations:

```csharp
public static class OrderProcessingWorkflow
{
    public record OrderProcessingResult
    {
        public bool IsSuccess { get; }
        public string? ErrorMessage { get; }
        public Guid? OrderId { get; }
        
        private OrderProcessingResult(bool isSuccess, string? errorMessage, Guid? orderId)
        {
            IsSuccess = isSuccess;
            ErrorMessage = errorMessage;
            OrderId = orderId;
        }
        
        public static OrderProcessingResult Success(Guid orderId) => new(true, null, orderId);
        public static OrderProcessingResult Failure(string errorMessage) => new(false, errorMessage, null);
    }
    
    public static async Task<OrderProcessingResult> ProcessOrder(
        CreateOrderCommand command,
        ISekibanExecutor executor)
    {
        // 1. Check if customer exists
        var customerExists = await executor.QueryAsync(
            new CustomerExistsQuery(command.CustomerId)).UnwrapBox();
            
        if (!customerExists)
        {
            return OrderProcessingResult.Failure($"Customer '{command.CustomerId}' not found");
        }
        
        // 2. Check product inventory for each item
        foreach (var item in command.Items)
        {
            var inventory = await executor.QueryAsync(
                new GetProductInventoryQuery(item.ProductId)).UnwrapBox();
                
            if (inventory < item.Quantity)
            {
                return OrderProcessingResult.Failure(
                    $"Insufficient inventory for product '{item.ProductId}'. " +
                    $"Requested: {item.Quantity}, Available: {inventory}");
            }
        }
        
        // 3. Create the order
        var orderResult = await executor.CommandAsync(command).UnwrapBox();
        var orderId = orderResult.PartitionKeys.AggregateId;
        
        // 4. Update inventory for each product
        foreach (var item in command.Items)
        {
            await executor.CommandAsync(new DecrementInventoryCommand(
                item.ProductId, 
                item.Quantity, 
                orderId));
        }
        
        // 5. Return success result with order ID
        return OrderProcessingResult.Success(orderId);
    }
}
```

## Implementing a Saga Pattern with Workflows

For more complex business processes that may require compensation/rollback, you can implement the Saga pattern:

```csharp
public static class PaymentProcessingSaga
{
    public record PaymentProcessingResult
    {
        public bool IsSuccess { get; }
        public string? ErrorMessage { get; }
        public Guid? TransactionId { get; }
        
        private PaymentProcessingResult(bool isSuccess, string? errorMessage, Guid? transactionId)
        {
            IsSuccess = isSuccess;
            ErrorMessage = errorMessage;
            TransactionId = transactionId;
        }
        
        public static PaymentProcessingResult Success(Guid transactionId) => 
            new(true, null, transactionId);
            
        public static PaymentProcessingResult Failure(string errorMessage) => 
            new(false, errorMessage, null);
    }
    
    public static async Task<PaymentProcessingResult> ProcessPayment(
        ProcessPaymentCommand command,
        ISekibanExecutor executor)
    {
        // 1. Reserve funds from customer account
        var reserveResult = await executor.CommandAsync(
            new ReserveFundsCommand(command.AccountId, command.Amount, command.OrderId)).UnwrapBox();
            
        if (reserveResult is CommandExecutionError error)
        {
            return PaymentProcessingResult.Failure($"Failed to reserve funds: {error.Message}");
        }
        
        try
        {
            // 2. Charge the payment provider
            var chargeResult = await executor.CommandAsync(
                new ChargePaymentProviderCommand(command.PaymentMethod, command.Amount)).UnwrapBox();
                
            if (chargeResult is CommandExecutionError chargeError)
            {
                // Compensation: Release reserved funds
                await executor.CommandAsync(
                    new ReleaseFundsCommand(command.AccountId, command.Amount, command.OrderId));
                    
                return PaymentProcessingResult.Failure($"Payment provider error: {chargeError.Message}");
            }
            
            // 3. Confirm the payment
            var confirmResult = await executor.CommandAsync(
                new ConfirmPaymentCommand(command.OrderId, command.Amount)).UnwrapBox();
                
            return PaymentProcessingResult.Success(confirmResult.PartitionKeys.AggregateId);
        }
        catch (Exception ex)
        {
            // Compensation: Release reserved funds
            await executor.CommandAsync(
                new ReleaseFundsCommand(command.AccountId, command.Amount, command.OrderId));
                
            return PaymentProcessingResult.Failure($"Unexpected error: {ex.Message}");
        }
    }
}
```

## Testing Workflows

Workflows can be tested using the same in-memory testing approach as other Sekiban components:

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

**Key Points**:
- Use `SekibanInMemoryTestBase` for testing workflows
- The base class provides an `Executor` property that implements `ISekibanExecutor`
- Use `GivenCommand` to set up the test state
- Test both success and failure scenarios

## Best Practices for Workflows

1. **Keep Workflows Stateless**: Workflows should be stateless and should delegate state management to aggregates
2. **Use Dependency Injection**: Use dependency injection to inject services into workflows
3. **Domain-Specific Result Types**: Return domain-specific result types that encapsulate success/failure information
4. **Error Handling**: Handle errors at the appropriate level, either in the workflow or in the API endpoint
5. **Test Thoroughly**: Test workflows thoroughly, including edge cases and error scenarios
6. **Consider Idempotency**: Make workflows idempotent when possible to handle retries
7. **Use Compensating Actions**: For complex workflows, implement compensating actions to rollback partial changes