AspireEventSample.ApiService/Aggregates/ReadModel/ShoppingCartReadModelHandler.cs
need to refactor


ICartReadModelAccessor _inMemoryWriter; 
this is no longer needed to maintain. only postgres ReadModelAccessors will be maintain.

First just read necessary document and write you plan so I can review.

Append your plan to 
clinerules_bank/tasks/013_memory.md
------------------------------------

## Implementation Plan for Removing In-Memory Writer

After reviewing the current implementation, here's my plan to refactor the ShoppingCartReadModelHandler to remove the in-memory writer:

### 1. Update ShoppingCartReadModelHandler Class

Remove the in-memory writer dependency and all related operations:

```csharp
public class ShoppingCartReadModelHandler : IReadModelHandler
{
    // Remove this field
    // private readonly ICartReadModelAccessor _inMemoryWriter;
    
    private readonly CartEntityPostgresWriter _postgresReadModelAccessorGrain;
    private readonly CartItemEntityPostgresWriter _cartItemPostgresWriter;
    private readonly IEventContextProvider _eventContextProvider;
    private readonly ILogger<ShoppingCartReadModelHandler> _logger;

    // Remove the inMemoryWriter parameter
    public ShoppingCartReadModelHandler(
        // ICartReadModelAccessor inMemoryWriter,
        CartEntityPostgresWriter postgresReadModelAccessorGrain,
        CartItemEntityPostgresWriter cartItemPostgresWriter,
        IEventContextProvider eventContextProvider,
        ILogger<ShoppingCartReadModelHandler> logger)
    {
        // _inMemoryWriter = inMemoryWriter;
        _postgresReadModelAccessorGrain = postgresReadModelAccessorGrain;
        _cartItemPostgresWriter = cartItemPostgresWriter;
        _eventContextProvider = eventContextProvider;
        _logger = logger;
    }
}
```

### 2. Update Event Handling Methods

#### HandleShoppingCartCreatedAsync
```csharp
private async Task HandleShoppingCartCreatedAsync(ShoppingCartCreated @event)
{
    var context = _eventContextProvider.GetCurrentEventContext();

    _logger.LogInformation(
        "Processing ShoppingCartCreated event for user {UserId} with cart ID {CartId}",
        @event.UserId,
        context.TargetId);

    // Remove in-memory entity creation and saving
    // Only create and save Postgres entity
    var postgresEntity = new CartDbRecord
    {
        Id = Guid.NewGuid(),
        TargetId = context.TargetId,
        RootPartitionKey = context.RootPartitionKey,
        AggregateGroup = context.AggregateGroup,
        LastSortableUniqueId = context.SortableUniqueId,
        TimeStamp = DateTime.UtcNow,
        UserId = @event.UserId,
        Status = "Created",
        TotalAmount = 0
    };

    await _postgresReadModelAccessorGrain.AddOrUpdateEntityAsync(postgresEntity);
}
```

#### HandleShoppingCartItemAddedAsync
```csharp
private async Task HandleShoppingCartItemAddedAsync(ShoppingCartItemAdded @event)
{
    var context = _eventContextProvider.GetCurrentEventContext();

    _logger.LogInformation(
        "Processing ShoppingCartItemAdded event for cart ID {CartId}, item: {ItemName}",
        context.TargetId,
        @event.Name);

    // Remove in-memory entity retrieval, updating, and saving
    // Only update Postgres entity
    var postgresEntity = await _postgresReadModelAccessorGrain.GetEntityByIdAsync(
        context.RootPartitionKey,
        context.AggregateGroup,
        context.TargetId);

    if (postgresEntity != null)
    {
        // Create a new cart item record
        var cartItemEntity = new CartItemDbRecord
        {
            Id = Guid.NewGuid(),
            TargetId = context.TargetId,
            RootPartitionKey = context.RootPartitionKey,
            AggregateGroup = context.AggregateGroup,
            LastSortableUniqueId = context.SortableUniqueId,
            TimeStamp = DateTime.UtcNow,
            CartId = postgresEntity.TargetId, // Link to the cart
            Name = @event.Name,
            Quantity = @event.Quantity,
            ItemId = @event.ItemId,
            Price = @event.Price
        };

        // Get all cart items to calculate total amount
        var existingItems = await _cartItemPostgresWriter.GetItemsByCartIdAsync(postgresEntity.TargetId);
        existingItems.Add(cartItemEntity); // Add the new item to the list for calculation
        
        // Calculate total amount
        var totalAmount = existingItems.Sum(item => item.Price * item.Quantity);

        // Update cart entity with new total amount
        postgresEntity.LastSortableUniqueId = context.SortableUniqueId;
        postgresEntity.TimeStamp = DateTime.UtcNow;
        postgresEntity.TotalAmount = totalAmount;

        // Save both entities
        await Task.WhenAll(
            _postgresReadModelAccessorGrain.AddOrUpdateEntityAsync(postgresEntity),
            _cartItemPostgresWriter.AddOrUpdateEntityAsync(cartItemEntity)
        );
    }
}
```

#### HandleShoppingCartPaymentProcessedAsync
```csharp
private async Task HandleShoppingCartPaymentProcessedAsync(ShoppingCartPaymentProcessed @event)
{
    var context = _eventContextProvider.GetCurrentEventContext();

    _logger.LogInformation(
        "Processing ShoppingCartPaymentProcessed event for cart ID {CartId}",
        context.TargetId);

    // Remove in-memory entity retrieval, updating, and saving
    // Only update Postgres entity
    var postgresEntity = await _postgresReadModelAccessorGrain.GetEntityByIdAsync(
        context.RootPartitionKey,
        context.AggregateGroup,
        context.TargetId);

    if (postgresEntity != null)
    {
        postgresEntity.LastSortableUniqueId = context.SortableUniqueId;
        postgresEntity.TimeStamp = DateTime.UtcNow;
        postgresEntity.Status = "Paid";

        await _postgresReadModelAccessorGrain.AddOrUpdateEntityAsync(postgresEntity);
    }
}
```

### 3. Update EventConsumerGrain

Remove the cartWriter parameter when creating the ShoppingCartReadModelHandler:

```csharp
public override async Task OnActivateAsync(CancellationToken cancellationToken)
{
    _logger.LogInformation("Activating EventConsumerGrain");

    // Get grain references
    var branchWriter = GrainFactory.GetGrain<IBranchEntityPostgresReadModelAccessorGrain>("default");
    // Remove this line
    // var cartWriter = GrainFactory.GetGrain<ICartReadModelAccessor>("default");
    var cartPostgresWriter = GrainFactory.GetGrain<CartEntityPostgresWriter>("default");

    // Create handlers directly
    _handlers.Add(
        new BranchReadModelHandler(
            branchWriter,
            _eventContextProvider,
            _loggerFactory.CreateLogger<BranchReadModelHandler>()));

    // Get cart item writer grain
    var cartItemPostgresWriter = GrainFactory.GetGrain<CartItemEntityPostgresWriter>("default");

    _handlers.Add(
        new ShoppingCartReadModelHandler(
            // Remove cartWriter parameter
            cartPostgresWriter,
            cartItemPostgresWriter,
            _eventContextProvider,
            _loggerFactory.CreateLogger<ShoppingCartReadModelHandler>()));

    // Rest of the method remains the same
}
```

### 4. Clean Up (Optional)

If the ICartReadModelAccessor interface and its implementations are no longer used elsewhere in the application, they can be removed as well.

This refactoring will eliminate the in-memory writer while maintaining all the functionality with the Postgres database.
