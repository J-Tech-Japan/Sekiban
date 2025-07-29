# Multiple Aggregate Projector - Sekiban Event Sourcing

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Aggregate, Projector, Command and Events](03_aggregate_command_events.md)
> - [Multiple Aggregate Projector](04_multiple_aggregate_projector.md) (You are here)
> - [Query](05_query.md)
> - [Workflow](06_workflow.md)
> - [JSON and Orleans Serialization](07_json_orleans_serialization.md)
> - [API Implementation](08_api_implementation.md)
> - [Client API (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md)
> - [Dapr Setup](11_dapr_setup.md)
> - [Unit Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Object](15_value_object.md)
> - [Deployment Guide](16_deployment.md)

## Multiple Aggregate Projectors

While single aggregate projectors are focused on building the state of a single entity, multiple aggregate projectors allow you to create views that combine data from multiple aggregates or create specialized projections.

## When to Use Multiple Aggregate Projectors

1. **Cross-Aggregate Views**: When you need to create a view that combines data from multiple aggregates
2. **Specialized Projections**: When you need a specialized view of aggregate data (e.g., statistics, denormalized views)
3. **Filtered Collections**: When you need a filtered subset of aggregates based on certain criteria
4. **Real-time Dashboard Data**: When you need to maintain counters or summaries across aggregates

## Built-in Multi-Projectors

Sekiban provides several built-in multi-projector types:

### 1. AggregateListProjector

This is the most commonly used multi-projector, which maintains a collection of all aggregates of a specific type:

```csharp
// This is built-in and doesn't need to be defined
public class AggregateListProjector<TProjector> : IMultiProjector
    where TProjector : IAggregateProjector
{
    public Dictionary<Guid, Aggregate> Aggregates { get; }
}
```

**Usage in queries**:
```csharp
[GenerateSerializer]
public record ListYourEntitiesQuery() 
    : IMultiProjectionListQuery<AggregateListProjector<YourEntityProjector>, ListYourEntitiesQuery, YourEntityResult>
{
    public static ResultBox<IEnumerable<YourEntityResult>> HandleFilter(
        MultiProjectionState<AggregateListProjector<YourEntityProjector>> state,
        ListYourEntitiesQuery query,
        IQueryContext context)
    {
        return state.Payload.Aggregates
            .Where(m => m.Value.GetPayload() is YourEntity)
            .Select(m => MapToResult((YourEntity)m.Value.GetPayload(), m.Key))
            .ToResultBox();
    }
    
    private static YourEntityResult MapToResult(YourEntity entity, Guid id) =>
        new(id, entity.Name, entity.Description);
        
    // Other required methods...
}
```

### 2. EventHistoryProjector

Maintains the full history of events for an aggregate:

```csharp
// This is built-in and doesn't need to be defined
public class EventHistoryProjector<TProjector> : IMultiProjector
    where TProjector : IAggregateProjector
{
    public Dictionary<Guid, List<IEvent>> EventHistories { get; }
}
```

**Usage example**:
```csharp
[GenerateSerializer]
public record GetEventHistoryQuery(Guid AggregateId)
    : IMultiProjectionQuery<EventHistoryProjector<YourEntityProjector>, GetEventHistoryQuery, List<EventHistoryItem>>
{
    public static ResultBox<List<EventHistoryItem>> HandleQuery(
        MultiProjectionState<EventHistoryProjector<YourEntityProjector>> state,
        GetEventHistoryQuery query,
        IQueryContext context)
    {
        if (!state.Payload.EventHistories.TryGetValue(query.AggregateId, out var events))
        {
            return new List<EventHistoryItem>();
        }
        
        return events
            .Select(e => new EventHistoryItem(
                e.Id, 
                e.Timestamp, 
                e.Version, 
                e.GetPayload().GetType().Name))
            .ToList();
    }
}

[GenerateSerializer]
public record EventHistoryItem(Guid Id, DateTime Timestamp, int Version, string EventType);
```

## Creating Custom Multi-Projectors

You can create custom multi-projectors to maintain specialized views of your domain:

```csharp
using Orleans.Serialization.Attributes;
using Sekiban.Pure.Projectors;
using System;
using System.Collections.Generic;
using System.Linq;

[GenerateSerializer]
public class OrderStatisticsProjector : IMultiProjector
{
    [Id(0)]
    public int TotalOrders { get; private set; }
    
    [Id(1)]
    public decimal TotalRevenue { get; private set; }
    
    [Id(2)]
    public Dictionary<string, int> ProductSales { get; private set; } = new();
    
    [Id(3)]
    public Dictionary<string, CustomerStats> CustomerStatistics { get; private set; } = new();
    
    [GenerateSerializer]
    public record CustomerStats(int OrderCount, decimal TotalSpent);
}
```

**Implementing the projector**:

```csharp
public class OrderStatisticsProjectorSubscriber : IMultiProjectorEventSubscriber
{
    public MultiProjectorSubscribers GetSubscribers() => new()
    {
        GetAggregateSubscriber<OrderProjector>(),
        GetAggregateSubscriber<ProductProjector>()
    };
    
    private EventSubscriber<OrderStatisticsProjector> GetAggregateSubscriber<TProjector>()
        where TProjector : IAggregateProjector, new()
    {
        var subscriber = new EventSubscriber<OrderStatisticsProjector>();
        
        subscriber.Subscribe<OrderPlaced, TProjector>((state, ev, aggregate) =>
        {
            var orderPlaced = (OrderPlaced)ev.GetPayload();
            
            state.Payload.TotalOrders++;
            state.Payload.TotalRevenue += orderPlaced.TotalAmount;
            
            // Update customer statistics
            if (!state.Payload.CustomerStatistics.TryGetValue(orderPlaced.CustomerId, out var customerStats))
            {
                customerStats = new CustomerStats(0, 0);
            }
            
            state.Payload.CustomerStatistics[orderPlaced.CustomerId] = new CustomerStats(
                customerStats.OrderCount + 1,
                customerStats.TotalSpent + orderPlaced.TotalAmount
            );
            
            // Update product sales
            foreach (var item in orderPlaced.Items)
            {
                if (!state.Payload.ProductSales.TryGetValue(item.ProductId, out var count))
                {
                    count = 0;
                }
                
                state.Payload.ProductSales[item.ProductId] = count + item.Quantity;
            }
        });
        
        // Subscribe to other events as needed
        
        return subscriber;
    }
}
```

**Registering the multi-projector**:

The multi-projector will be automatically registered by the source generator if it implements `IMultiProjector`. You can then use it in queries:

```csharp
[GenerateSerializer]
public record GetOrderStatisticsQuery() 
    : IMultiProjectionQuery<OrderStatisticsProjector, GetOrderStatisticsQuery, OrderStatistics>
{
    public static ResultBox<OrderStatistics> HandleQuery(
        MultiProjectionState<OrderStatisticsProjector> state,
        GetOrderStatisticsQuery query,
        IQueryContext context)
    {
        return new OrderStatistics(
            state.Payload.TotalOrders,
            state.Payload.TotalRevenue,
            state.Payload.ProductSales.OrderByDescending(x => x.Value).Take(5).ToDictionary(k => k.Key, v => v.Value),
            state.Payload.CustomerStatistics.Count
        );
    }
}

[GenerateSerializer]
public record OrderStatistics(
    int TotalOrders,
    decimal TotalRevenue,
    Dictionary<string, int> TopSellingProducts,
    int TotalCustomers
);
```

## Performance Considerations

Multi-projectors can be resource-intensive, especially if they need to process many events or maintain large collections. Consider the following best practices:

1. **Be Selective with Events**: Only subscribe to the events you need for your projection
2. **Use Efficient Data Structures**: Choose appropriate data structures for your projection data
3. **Consider Snapshotting**: For large projections, consider implementing snapshotting
4. **Query Optimization**: Keep your query handlers efficient, especially for large datasets

## Testing Multi-Projectors

You can test multi-projectors using the same approaches as regular aggregates:

```csharp
[Fact]
public void OrderStatistics_ShouldUpdateCorrectly()
{
    // Given
    var orderCommand = new PlaceOrder("C001", new[]
    {
        new OrderItem("P001", 2, 10.0m),
        new OrderItem("P002", 1, 15.0m)
    });
    
    // When
    GivenCommand(orderCommand);
    
    // Then
    var statistics = ThenQuery(new GetOrderStatisticsQuery());
    
    Assert.Equal(1, statistics.TotalOrders);
    Assert.Equal(35.0m, statistics.TotalRevenue);
    Assert.Equal(2, statistics.TopSellingProducts.Count);
    Assert.Equal(1, statistics.TotalCustomers);
}
```