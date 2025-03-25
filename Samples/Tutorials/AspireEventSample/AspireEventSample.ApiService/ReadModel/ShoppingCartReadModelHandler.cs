using AspireEventSample.ApiService.Grains;
using AspireEventSample.Domain.Aggregates.Carts;
using AspireEventSample.ReadModels;
using Sekiban.Pure.Events;
namespace AspireEventSample.ApiService.ReadModel;

/// <summary>
///     Shopping cart read model handler
/// </summary>
public class ShoppingCartReadModelHandler : IReadModelHandler
{
    private readonly ICartEntityPostgresWriter _postgresReadModelAccessorGrain;
    private readonly ICartItemEntityPostgresWriter _cartItemPostgresWriterGrain;
    private readonly IEventContextProvider _eventContextProvider;
    private readonly ILogger<ShoppingCartReadModelHandler> _logger;

    public ShoppingCartReadModelHandler(
        ICartEntityPostgresWriter postgresReadModelAccessorGrain,
        ICartItemEntityPostgresWriter cartItemPostgresWriterGrain,
        IEventContextProvider eventContextProvider,
        ILogger<ShoppingCartReadModelHandler> logger)
    {
        _postgresReadModelAccessorGrain = postgresReadModelAccessorGrain;
        _cartItemPostgresWriterGrain = cartItemPostgresWriterGrain;
        _eventContextProvider = eventContextProvider;
        _logger = logger;
    }

    /// <summary>
    ///     Handle event
    /// </summary>
    public Task HandleEventAsync(IEvent @event) =>
        @event.GetPayload() switch
        {
            ShoppingCartCreated shoppingCartCreated =>
                HandleShoppingCartCreatedAsync(shoppingCartCreated),
            ShoppingCartItemAdded shoppingCartItemAdded =>
                HandleShoppingCartItemAddedAsync(shoppingCartItemAdded),
            ShoppingCartPaymentProcessed shoppingCartPaymentProcessed =>
                HandleShoppingCartPaymentProcessedAsync(shoppingCartPaymentProcessed),
            _ => Task.CompletedTask
        };

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
            TotalAmount = 0
        };

        // Save to Postgres repository
        await _postgresReadModelAccessorGrain.AddOrUpdateEntityAsync(postgresEntity);
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

        // Update Postgres entity
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
            var existingItems = await _cartItemPostgresWriterGrain.GetItemsByCartIdAsync(postgresEntity.TargetId);
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
                _cartItemPostgresWriterGrain.AddOrUpdateEntityAsync(cartItemEntity)
            );
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