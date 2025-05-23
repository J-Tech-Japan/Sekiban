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
> - [ResultBox](13_result_box.md)

## Workflows and Domain Services

Sekiban supports implementing domain workflows and services that encapsulate business logic that spans multiple aggregates or requires specialized processing.

## Domain Workflows

Domain workflows are stateless services that implement business processes that may involve multiple aggregates or complex validation logic. They are particularly useful for:

1. **Cross-Aggregate Operations**: When a business process spans multiple aggregates
2. **Complex Validation**: When validation requires checking against multiple aggregates or external systems
3. **Reusable Business Logic**: When the same logic is used in multiple places

```csharp
using Sekiban.Pure.Executors;
using EsCQRSQuestions.Domain.Aggregates.Questions.Commands;
using EsCQRSQuestions.Domain.Aggregates.Questions.Queries;
using EsCQRSQuestions.Domain.Projections.Questions;
using ResultBoxes;
using Sekiban.Pure.Command;
using System.Text.Json;
using Sekiban.Pure.Command.Executor;
namespace EsCQRSQuestions.Domain.Workflows;

/// <summary>
/// Workflow to manage question display. 🚀
/// </summary>
public class QuestionDisplayWorkflow(ISekibanExecutor executor)
{
    /// <summary>
    /// Workflow for exclusive control of question display. 🔒
    /// Stops any currently displayed questions in the group before displaying the specified question. 📊
    /// </summary>
    public Task<ResultBox<CommandResponseSimple>> StartDisplayQuestionExclusivelyAsync(
        Guid questionId)
    {
        return executor.QueryAsync(new QuestionsQuery(string.Empty))
            .Conveyor(result => result.Items.Any(q => q.QuestionId == questionId)
                ? result.Items.First(q => q.QuestionId == questionId).ToResultBox()
                : new Exception($"Question not found: {questionId}"))
            .Combine(detail => executor.QueryAsync(
                new QuestionsQuery(string.Empty, detail.QuestionGroupId)))
            .Do((detail, questions) => questions.Items.Where(q => q.IsDisplayed && q.QuestionId != questionId).ToList()
                .ToResultBox().ScanEach(async record =>
                {
                    await executor.CommandAsync(new StopDisplayCommand(record.QuestionId));
                }))
            .Conveyor(items => executor.CommandAsync(new StartDisplayCommand(questionId)).ToSimpleCommandResponse());
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
apiRoute
    .MapPost(
        "/questions/startDisplay",
        async (
            [FromBody] StartDisplayCommand command,
            [FromServices] SekibanOrleansExecutor executor) =>
        {
            var workflow = new QuestionDisplayWorkflow(executor);
            return await workflow.StartDisplayQuestionExclusivelyAsync(command.QuestionId).UnwrapBox();
        })
    .WithOpenApi()
    .WithName("StartDisplayQuestion");
```

## Implementing a Saga Pattern with Workflows

For more complex business processes that may require compensation/rollback, you can implement the Saga pattern:

```csharp
using Sekiban.Pure.Command.Executor;
using Sekiban.Core.Shared;
using Sekiban.Pure.Command;
using ResultBoxes;
using System.Threading.Tasks;

namespace OrderProcess.Domain.Workflows;

/// <summary>
/// Workflow implementing saga pattern for order processing. 🛒
/// Handles the coordination of steps for creating an order with compensation actions. 🔄
/// </summary>
public class OrderSagaWorkflow(ISekibanExecutor executor)
{
    private List<Func<Task<ResultBox<CommandResponseSimple>>>> _compensationActions = new();

    /// <summary>
    /// Process a complete order using saga pattern with compensation actions. 📦
    /// If any step fails, previously executed steps will be rolled back. ⏮️
    /// </summary>
    public async Task<ResultBox<CommandResponseSimple>> ProcessOrderAsync(
        Guid orderId, 
        Guid productId, 
        int quantity, 
        Guid customerId, 
        decimal totalAmount)
    {
        // Process involves three steps:
        // 1. Reserve inventory
        // 2. Process payment
        // 3. Create shipment
        
        // Step 1: Reserve inventory
        var reserveInventoryResult = await executor.CommandAsync(
            new ReserveInventoryCommand(productId, quantity));
            
        if (!reserveInventoryResult.IsSuccess)
        {
            // No compensation needed for first step
            return new Exception("Failed to reserve inventory").ToResultBox<CommandResponseSimple>();
        }
        
        // Register compensation action
        _compensationActions.Add(() => executor.CommandAsync(
            new ReleaseInventoryCommand(productId, quantity)));
            
        // Step 2: Process payment
        var paymentId = Guid.NewGuid();
        var processPaymentResult = await executor.CommandAsync(
            new ProcessPaymentCommand(paymentId, customerId, totalAmount, orderId));
            
        if (!processPaymentResult.IsSuccess)
        {
            // Execute compensation - release inventory
            await ExecuteCompensationActionsAsync();
            return new Exception("Failed to process payment").ToResultBox<CommandResponseSimple>();
        }
        
        // Register compensation action
        _compensationActions.Add(() => executor.CommandAsync(
            new RefundPaymentCommand(paymentId)));
            
        // Step 3: Create shipment
        var shipmentResult = await executor.CommandAsync(
            new CreateShipmentCommand(orderId, customerId));
            
        if (!shipmentResult.IsSuccess)
        {
            // Execute compensation - refund payment and release inventory
            await ExecuteCompensationActionsAsync();
            return new Exception("Failed to create shipment").ToResultBox<CommandResponseSimple>();
        }
        
        // All steps completed successfully
        _compensationActions.Clear();
        return reserveInventoryResult.ToSimpleCommandResponse();
    }
    
    /// <summary>
    /// Execute all compensation actions in reverse order. 🔙
    /// This ensures proper rollback of completed steps. 🔄
    /// </summary>
    private async Task ExecuteCompensationActionsAsync()
    {
        // Execute compensating actions in reverse order
        foreach (var action in _compensationActions.AsEnumerable().Reverse())
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                // Log failed compensation - in real system you might want to use
                // a persistent saga log to track and retry these failures
                Console.WriteLine($"Compensation action failed: {ex.Message}");
            }
        }
        _compensationActions.Clear();
    }
}
```

## Testing Workflows

Workflows can be tested using the same in-memory testing approach as other Sekiban components. The `SekibanInMemoryTestBase` class provides all the necessary infrastructure for testing in isolation. 🧪

### Testing Saga Pattern Workflows

Here's an example of testing the OrderSagaWorkflow we created earlier:

```csharp
using Xunit;
using System.Threading.Tasks;
using OrderProcess.Domain.Aggregates.Inventory.Commands;
using OrderProcess.Domain.Aggregates.Payment.Commands;
using OrderProcess.Domain.Aggregates.Shipment.Commands;
using OrderProcess.Domain.Aggregates.Inventory.Projections;
using OrderProcess.Domain.Aggregates.Payment.Projections;
using OrderProcess.Domain.Workflows;
using Sekiban.Pure.xUnit;
using Sekiban.Core.Shared;

namespace OrderProcess.Tests.Workflows;

/// <summary>
/// Test cases for the Order Saga Workflow. 🧪
/// Tests both successful and failure scenarios with proper compensation actions. 🔄
/// </summary>
public class OrderSagaWorkflowTests : SekibanInMemoryTestBase
{
    protected override SekibanDomainTypes GetDomainTypes() => 
        OrderProcessDomainTypes.Generate(OrderProcessEventsJsonContext.Default.Options);

    [Fact]
    public async Task ProcessOrder_AllStepsSucceed_OrderIsCreated()
    {
        // Arrange - setup test data 📋
        var productId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var initialStock = 10;
        
        // Setup initial inventory stock
        GivenCommand(new CreateInventoryItemCommand(productId, "Test Product", initialStock));
        
        // Setup customer account with funds
        GivenCommand(new CreateCustomerCommand(customerId, "Test Customer", 1000m));
        
        // Act - execute the workflow 🚀
        var workflow = new OrderSagaWorkflow(Executor);
        var result = await workflow.ProcessOrderAsync(orderId, productId, 5, customerId, 100m);
        
        // Assert - verify the outcomes ✅
        Assert.True(result.IsSuccess);
        
        // Verify inventory was updated
        var inventory = ThenQuery(new GetInventoryItemQuery(productId));
        Assert.Equal(initialStock - 5, inventory.AvailableStock);
        
        // Verify payment was processed
        var customerPayments = ThenQuery(new GetCustomerPaymentsQuery(customerId));
        Assert.Contains(customerPayments.Payments, p => p.OrderId == orderId && p.Amount == 100m);
        
        // Verify shipment was created
        var shipment = ThenQuery(new GetShipmentByOrderIdQuery(orderId));
        Assert.Equal(orderId, shipment.OrderId);
        Assert.Equal(customerId, shipment.CustomerId);
    }
    
    [Fact]
    public async Task ProcessOrder_PaymentFails_InventoryIsRestored()
    {
        // Arrange - setup test data with insufficient funds 📋
        var productId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var initialStock = 10;
        
        // Setup initial inventory stock
        GivenCommand(new CreateInventoryItemCommand(productId, "Test Product", initialStock));
        
        // Setup customer account with insufficient funds
        GivenCommand(new CreateCustomerCommand(customerId, "Test Customer", 50m)); // Only 50, but order costs 100
        
        // Act - execute the workflow 🚀
        var workflow = new OrderSagaWorkflow(Executor);
        var result = await workflow.ProcessOrderAsync(orderId, productId, 5, customerId, 100m);
        
        // Assert - verify the outcomes ✅
        Assert.False(result.IsSuccess);
        
        // Verify inventory was restored (compensation action worked)
        var inventory = ThenQuery(new GetInventoryItemQuery(productId));
        Assert.Equal(initialStock, inventory.AvailableStock); // Should be back to initial value
        
        // Verify no payment was processed
        var customerPayments = ThenQuery(new GetCustomerPaymentsQuery(customerId));
        Assert.DoesNotContain(customerPayments.Payments, p => p.OrderId == orderId);
        
        // Verify no shipment was created
        Assert.Throws<Exception>(() => ThenQuery(new GetShipmentByOrderIdQuery(orderId)));
    }
}
```

### Testing Regular Workflows

Here's a simpler example of testing the QuestionDisplayWorkflow:

```csharp
using Xunit;
using System.Threading.Tasks;
using EsCQRSQuestions.Domain.Workflows;
using EsCQRSQuestions.Domain.Aggregates.Questions.Commands;
using EsCQRSQuestions.Domain.Projections.Questions;
using Sekiban.Pure.xUnit;
using Sekiban.Core.Shared;

namespace EsCQRSQuestions.Tests.Workflows;

public class QuestionDisplayWorkflowTests : SekibanInMemoryTestBase
{
    protected override SekibanDomainTypes GetDomainTypes() => 
        EsCQRSQuestionsDomainTypes.Generate(EsCQRSQuestionsEventsJsonContext.Default.Options);

    [Fact]
    public async Task StartDisplayQuestionExclusively_StopsCurrentlyDisplayedQuestions()
    {
        // Arrange - create questions in the same group
        var groupId = Guid.NewGuid();
        var question1Id = Guid.NewGuid();
        var question2Id = Guid.NewGuid();
        
        // Create two questions in the same group
        GivenCommand(new CreateQuestionCommand(question1Id, "Question 1", "Content 1", groupId));
        GivenCommand(new CreateQuestionCommand(question2Id, "Question 2", "Content 2", groupId));
        
        // Display the first question
        GivenCommand(new StartDisplayCommand(question1Id));
        
        // Verify question1 is displayed
        var questionsBeforeTest = ThenQuery(new QuestionsQuery(string.Empty, groupId));
        var displayedQuestionsBefore = questionsBeforeTest.Items.Where(q => q.IsDisplayed).ToList();
        Assert.Single(displayedQuestionsBefore);
        Assert.Equal(question1Id, displayedQuestionsBefore[0].QuestionId);
        
        // Act - execute the workflow to display question2
        var workflow = new QuestionDisplayWorkflow(Executor);
        var result = await workflow.StartDisplayQuestionExclusivelyAsync(question2Id);
        
        // Assert - question1 should be stopped, question2 should be displayed
        Assert.True(result.IsSuccess);
        
        var questionsAfterTest = ThenQuery(new QuestionsQuery(string.Empty, groupId));
        var displayedQuestionsAfter = questionsAfterTest.Items.Where(q => q.IsDisplayed).ToList();
        Assert.Single(displayedQuestionsAfter);
        Assert.Equal(question2Id, displayedQuestionsAfter[0].QuestionId);
        
        // Verify question1 is no longer displayed
        var question1 = questionsAfterTest.Items.First(q => q.QuestionId == question1Id);
        Assert.False(question1.IsDisplayed);
    }
}
```

**Key Points**:
- Use `SekibanInMemoryTestBase` for testing workflows. 🧪
- The base class provides an `Executor` property that implements `ISekibanExecutor`. 🔧
- Use `GivenCommand` to set up the test state. 🏗️ 
- Use `ThenQuery` to verify the outcomes of workflow execution. 🔍
- Test both success and failure scenarios. ✓✗
- For Saga Pattern workflows, ensure compensation actions work correctly. ↩️

## Best Practices for Workflows

1. **Keep Workflows Stateless**: Workflows should be stateless and should delegate state management to aggregates
2. **Use Dependency Injection**: Use dependency injection to inject services into workflows
3. **Domain-Specific Result Types**: Return domain-specific result types that encapsulate success/failure information
4. **Error Handling**: Handle errors at the appropriate level, either in the workflow or in the API endpoint
5. **Test Thoroughly**: Test workflows thoroughly, including edge cases and error scenarios
6. **Consider Idempotency**: Make workflows idempotent when possible to handle retries
7. **Use Compensating Actions**: For complex workflows, implement compensating actions to rollback partial changes