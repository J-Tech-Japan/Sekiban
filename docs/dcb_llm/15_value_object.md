# Value Objects - Modeling Shared Concepts

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Commands, Events, Tags, Projectors](03_aggregate_command_events.md)
> - [MultiProjection](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Command Workflow](06_workflow.md)
> - [Serialization & Domain Types](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md)
> - [Client UI (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md)
> - [Storage Providers](11_storage_providers.md)
> - [Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md) (You are here)
> - [Deployment Guide](16_deployment.md)

Value objects in DCB follow the same principles as Domain-Driven Design: immutable records that encapsulate invariants and
behaviors shared across commands, events, and projections.

## Using Records

C# records provide built-in value equality and with-expression cloning. Use them for:

- Command payload fragments (e.g., address details, money amounts)
- Event data structures
- Tag state payload sub-components

```csharp
public record Capacity(int Value)
{
    public static Capacity FromInt(int value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        return new Capacity(value);
    }

    public Capacity Decrease() => this with { Value = Value - 1 };
}
```

## Serialization Considerations

Register custom converters when value objects need special serialization:

```csharp
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
options.Converters.Add(new CapacityJsonConverter());
var domainTypes = DcbDomainTypes.Simple(configure, options);
```

Ensure converter handles both serialization and deserialization so events persist correctly across backends.

## Invariants in Value Objects

Push validation into factory methods rather than command handlers. This keeps commands focused on orchestration while
value objects guarantee correctness.

```csharp
public record EnrollmentWindow(DateOnly Start, DateOnly End)
{
    public static EnrollmentWindow Create(DateOnly start, DateOnly end)
    {
        if (start > end) throw new ArgumentException("Start must be before End");
        return new EnrollmentWindow(start, end);
    }

    public bool IsOpen(DateOnly today) => today >= Start && today <= End;
}
```

Projectors can then rely on value object helpers when applying events.

## Sharing Between Commands and Queries

Keep value objects light so they can be shared in query responses. If you need different serialization for API output,
create DTOs that wrap the domain value object.

## Testing Value Objects

Unit test factory methods and behaviors directly. Because they are immutable, verifying equality and `with` behavior is
straightforward.

## When to Avoid

- High-volume events where the value object adds serialization overhead with no business benefit.
- Cross-service boundaries that expect primitive types (consider mapping to API DTOs instead).
