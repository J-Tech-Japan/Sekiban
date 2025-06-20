# Value Object - Sekiban Event Sourcing

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
> - [Unit Testing](11_unit_testing.md)
> - [Common Issues and Solutions](12_common_issues.md)
> - [ResultBox](13_result_box.md)
> - [Value Object](14_value_object.md) (You are here)

## Value Object

Value Objects are an important concept in Domain-Driven Design (DDD), representing objects whose equality is based on their values rather than identity. In Sekiban, Value Objects can be leveraged to provide strong typing and automatic validation.

## Basic Implementation Approach

Value Objects in Sekiban should be implemented following these principles:

### 1. Using Record Types

Define Value Objects using C# record types:

```csharp
using System.ComponentModel.DataAnnotations;

namespace YourDomain.ValueObjects;

[GenerateSerializer]
public record TemperatureCelsius([property:Range(-273.15, 1000000.0, ErrorMessage = "Temperature cannot be below absolute zero (-273.15°C)")] double Value)
{
    /// <summary>
    /// Converts Celsius to Fahrenheit
    /// </summary>
    public double GetFahrenheit() => Value * 9 / 5 + 32;
}
```

### 2. Attribute-Based Validation

Always use `System.ComponentModel.DataAnnotations` attributes for Value Object validation:

```csharp
[GenerateSerializer]
public record Email([property:EmailAddress(ErrorMessage = "Invalid email format")] string Value);

[GenerateSerializer]
public record Age([property:Range(0, 150, ErrorMessage = "Age must be between 0 and 150")] int Value);

[GenerateSerializer]
public record ProductName([property:Required, property:StringLength(100, MinimumLength = 1, ErrorMessage = "Product name must be between 1 and 100 characters")] string Value);
```

### 3. Important Constraints

#### ❌ No Validation in Constructor

Do not throw exceptions in constructors:

```csharp
// ❌ Bad example - Don't do this
[GenerateSerializer]
public record Price(decimal Value)
{
    public Price(decimal Value) : this()
    {
        if (Value < 0)
            throw new ArgumentException("Price cannot be negative"); // This is NOT allowed!
    }
}
```

#### ❌ No Validation in Static Properties

Avoid validation in static properties or methods:

```csharp
// ❌ Bad example - Don't do this
[GenerateSerializer]
public record UserId(string Value)
{
    public static UserId Create(string value)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("UserId cannot be null or empty"); // This is also NOT allowed!
        
        return new UserId(value);
    }
}
```

### 4. Why Attribute-Based Validation is Required

#### Event Replay Issues

In Sekiban's event sourcing, event replay occurs when reconstructing aggregates from past events. If validation is performed in constructors or static methods, previously valid data might fail current validation rules.

#### Correct Flow

1. **Command Input**: Attribute-based validation is executed
2. **Event Generation**: Events are created with validated data
3. **Event Replay**: Objects are constructed directly without validation

### 5. Complex Value Object Example

A more complex Value Object example:

```csharp
[GenerateSerializer]
public record Money(
    [property:Range(0, double.MaxValue, ErrorMessage = "Amount must be positive")]
    decimal Amount,
    
    [property:RegularExpression(@"^[A-Z]{3}$", ErrorMessage = "Currency must be a 3-letter code")]
    string CurrencyCode)
{
    /// <summary>
    /// Converts currency
    /// </summary>
    public Money ConvertTo(string newCurrencyCode, decimal exchangeRate)
    {
        return new Money(Amount * exchangeRate, newCurrencyCode);
    }

    /// <summary>
    /// Formats the amount for display
    /// </summary>
    public string FormatDisplay() => $"{Amount:F2} {CurrencyCode}";
}
```

### 6. Best Practices

#### Orleans Serialization Support

Always add the `[GenerateSerializer]` attribute:

```csharp
[GenerateSerializer]
public record YourValueObject(...);
```

#### Maintain Immutability

Value Objects should be immutable. Using record types automatically ensures immutability.

#### Add Meaningful Methods

Value Objects can include methods that are meaningful to their domain:

```csharp
[GenerateSerializer]
public record Distance([property:Range(0, double.MaxValue)] double Meters)
{
    public Distance AddMeters(double additionalMeters) => new(Meters + additionalMeters);
    public double ToKilometers() => Meters / 1000.0;
    public double ToMiles() => Meters * 0.000621371;
}
```

## Summary

For Value Object implementation in Sekiban:

- ✅ Use record types
- ✅ Use attribute-based validation
- ✅ Add `[GenerateSerializer]`
- ✅ Add meaningful methods
- ❌ Don't throw in constructors
- ❌ Don't validate in static properties

Following these principles will create safe Value Objects that are fully compatible with event sourcing.

## Using Value Objects in Events and Commands

Value Objects can also be used as persistent data in events and commands. This allows for clearer expression of domain concepts and improved type safety.

### Value Object Usage in Events

```csharp
[GenerateSerializer]
public record ProductPriceChanged(
    ProductId ProductId,
    Money OldPrice,
    Money NewPrice,
    DateTime ChangedAt) : IEvent;

[GenerateSerializer]
public record CustomerRegistered(
    CustomerId CustomerId,
    Email Email,
    CustomerName Name,
    Address ShippingAddress) : IEvent;
```

### Value Object Usage in Commands

```csharp
[GenerateSerializer]
public record ChangeProductPrice(
    ProductId ProductId,
    Money NewPrice) : ICommandWithHandler<ChangeProductPrice, ProductProjector>;

[GenerateSerializer]
public record RegisterCustomer(
    Email Email,
    CustomerName Name,
    Address ShippingAddress) : ICommandWithHandler<RegisterCustomer, CustomerProjector>;
```

### JSON Serialization Configuration

When using Value Objects in events and commands, they must also be added to your project's `JsonSerializerContext`.

Example: `YourDomainEventsJsonContext.cs`

```csharp
using System.Text.Json.Serialization;
using YourDomain.ValueObjects;
using YourDomain.Events;
using YourDomain.Commands;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;

namespace YourDomain;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(EventDocumentCommon))]
[JsonSerializable(typeof(EventDocumentCommon[]))]
// ... existing Sekiban types ...

// Add Value Objects
[JsonSerializable(typeof(Money))]
[JsonSerializable(typeof(Email))]
[JsonSerializable(typeof(CustomerName))]
[JsonSerializable(typeof(Address))]
[JsonSerializable(typeof(ProductId))]
[JsonSerializable(typeof(CustomerId))]

// Events using Value Objects
[JsonSerializable(typeof(EventDocument<ProductPriceChanged>))]
[JsonSerializable(typeof(ProductPriceChanged))]
[JsonSerializable(typeof(EventDocument<CustomerRegistered>))]
[JsonSerializable(typeof(CustomerRegistered))]

// Aggregates using Value Objects
[JsonSerializable(typeof(Product))]
[JsonSerializable(typeof(Customer))]
public partial class YourDomainEventsJsonContext : JsonSerializerContext
{
}
```

This configuration ensures that Value Objects are properly handled by both JSON and Orleans serialization.
