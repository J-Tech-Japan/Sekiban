using AspireEventSample.ApiService.Aggregates.Carts;
using AspireEventSample.ApiService.Grains;
using AspireEventSample.ReadModels;
using Sekiban.Pure.Events;
using System.Text.Json;
namespace AspireEventSample.ApiService.Aggregates.ReadModel;

/// <summary>
///     Shopping cart read model handler
/// </summary>
public class ShoppingCartReadModelHandler : IReadModelHandler
{
    private readonly ICartReadModelAccessor _inMemoryWriter;
    private readonly CartEntityPostgresWriter _postgresReadModelAccessorGrain;
    private readonly IEventContextProvider _eventContextProvider;
    private readonly ILogger<ShoppingCartReadModelHandler> _logger;

    public ShoppingCartReadModelHandler(
        ICartReadModelAccessor inMemoryWriter,
        CartEntityPostgresWriter postgresReadModelAccessorGrain,
        IEventContextProvider eventContextProvider,
        ILogger<ShoppingCartReadModelHandler> logger)
    {
        _inMemoryWriter = inMemoryWriter;
        _postgresReadModelAccessorGrain = postgresReadModelAccessorGrain;
        _eventContextProvider = eventContextProvider;
        _logger = logger;
    }

    /// <summary>
    ///     Handle event
    /// </summary>
    public async Task HandleEventAsync(IEvent @event)
    {
        var eventPayload = @event.GetPayload();

        // Switch based on event type
        switch (eventPayload)
        {
            case ShoppingCartCreated shoppingCartCreated:
                await HandleShoppingCartCreatedAsync(shoppingCartCreated);
                break;

            case ShoppingCartItemAdded shoppingCartItemAdded:
                await HandleShoppingCartItemAddedAsync(shoppingCartItemAdded);
                break;

            case ShoppingCartPaymentProcessed shoppingCartPaymentProcessed:
                await HandleShoppingCartPaymentProcessedAsync(shoppingCartPaymentProcessed);
                break;

            // Other event types can be handled here
        }
    }

    /// <summary>
    ///     Handle ShoppingCartCreated event
    /// </summary>
    private async Task HandleShoppingCartCreatedAsync(ShoppingCartCreated @event)
    {
        var context = _eventContextProvider.GetCurrentEventContext();

        _logger.LogInformation(
            "Processing ShoppingCartCreated event for user {UserId} with cart ID {CartId}",
            @event.UserId,
            context.TargetId);

        // Create in-memory entity
        var inMemoryEntity = new CartEntity
        {
            Id = Guid.NewGuid(),
            TargetId = context.TargetId,
            RootPartitionKey = context.RootPartitionKey,
            AggregateGroup = context.AggregateGroup,
            LastSortableUniqueId = context.SortableUniqueId,
            TimeStamp = DateTime.UtcNow,
            UserId = @event.UserId,
            Items = new List<ShoppingCartItems>(),
            Status = "Created",
            TotalAmount = 0
        };

        // Create Postgres entity
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
            TotalAmount = 0,
            ItemsJson = "[]"
        };

        // Save to both repositories
        await Task.WhenAll(
            _inMemoryWriter.AddOrUpdateEntityAsync(inMemoryEntity),
            _postgresReadModelAccessorGrain.AddOrUpdateEntityAsync(postgresEntity)
        );
    }

    /// <summary>
    ///     Handle ShoppingCartItemAdded event
    /// </summary>
    private async Task HandleShoppingCartItemAddedAsync(ShoppingCartItemAdded @event)
    {
        var context = _eventContextProvider.GetCurrentEventContext();

        _logger.LogInformation(
            "Processing ShoppingCartItemAdded event for cart ID {CartId}, item: {ItemName}",
            context.TargetId,
            @event.Name);

        // Update in-memory entity
        var inMemoryEntity = await _inMemoryWriter.GetEntityByIdAsync(
            context.RootPartitionKey,
            context.AggregateGroup,
            context.TargetId);

        if (inMemoryEntity != null)
        {
            var updatedItems = new List<ShoppingCartItems>(inMemoryEntity.Items)
            {
                new(@event.Name, @event.Quantity, @event.ItemId, @event.Price)
            };

            var totalAmount = updatedItems.Sum(item => item.Price * item.Quantity);

            var updatedInMemoryEntity = inMemoryEntity with
            {
                LastSortableUniqueId = context.SortableUniqueId,
                TimeStamp = DateTime.UtcNow,
                Items = updatedItems,
                TotalAmount = totalAmount
            };

            await _inMemoryWriter.AddOrUpdateEntityAsync(updatedInMemoryEntity);
        }

        // Update Postgres entity
        var postgresEntity = await _postgresReadModelAccessorGrain.GetEntityByIdAsync(
            context.RootPartitionKey,
            context.AggregateGroup,
            context.TargetId);

        if (postgresEntity != null)
        {
            // Get existing items from JSON
            List<ShoppingCartItems> items;
            if (string.IsNullOrEmpty(postgresEntity.ItemsJson) || postgresEntity.ItemsJson == "[]")
            {
                items = new List<ShoppingCartItems>();
            } else
            {
                items = JsonSerializer.Deserialize<List<ShoppingCartItems>>(postgresEntity.ItemsJson) ??
                    new List<ShoppingCartItems>();
            }

            // Add new item
            items.Add(new ShoppingCartItems(@event.Name, @event.Quantity, @event.ItemId, @event.Price));

            // Calculate total amount
            var totalAmount = items.Sum(item => item.Price * item.Quantity);

            // Update entity
            postgresEntity.LastSortableUniqueId = context.SortableUniqueId;
            postgresEntity.TimeStamp = DateTime.UtcNow;
            postgresEntity.ItemsJson = JsonSerializer.Serialize(items);
            postgresEntity.TotalAmount = totalAmount;

            await _postgresReadModelAccessorGrain.AddOrUpdateEntityAsync(postgresEntity);
        }
    }

    /// <summary>
    ///     Handle ShoppingCartPaymentProcessed event
    /// </summary>
    private async Task HandleShoppingCartPaymentProcessedAsync(ShoppingCartPaymentProcessed @event)
    {
        var context = _eventContextProvider.GetCurrentEventContext();

        _logger.LogInformation(
            "Processing ShoppingCartPaymentProcessed event for cart ID {CartId}",
            context.TargetId);

        // Update in-memory entity
        var inMemoryEntity = await _inMemoryWriter.GetEntityByIdAsync(
            context.RootPartitionKey,
            context.AggregateGroup,
            context.TargetId);

        if (inMemoryEntity != null)
        {
            var updatedInMemoryEntity = inMemoryEntity with
            {
                LastSortableUniqueId = context.SortableUniqueId,
                TimeStamp = DateTime.UtcNow,
                Status = "Paid"
            };

            await _inMemoryWriter.AddOrUpdateEntityAsync(updatedInMemoryEntity);
        }

        // Update Postgres entity
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
}