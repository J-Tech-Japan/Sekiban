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
    }

    [Fact]
    public async Task CheckUserIdDuplicate_WhenUserIdDoesNotExist_ReturnsSuccess()
    {
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