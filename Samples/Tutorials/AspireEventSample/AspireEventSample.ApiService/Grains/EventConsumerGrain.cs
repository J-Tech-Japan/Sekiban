using AspireEventSample.ApiService.Aggregates.Branches;
using AspireEventSample.ApiService.Aggregates.Carts;
using AspireEventSample.ApiService.Aggregates.ReadModel;
using AspireEventSample.ReadModels;
using Orleans.Streams;
using Sekiban.Pure.Events;
namespace AspireEventSample.ApiService.Grains;

[ImplicitStreamSubscription("AllEvents")]
public class EventConsumerGrain : Grain, IEventConsumerGrain
{
    private IAsyncStream<IEvent>? _stream;
    private StreamSubscriptionHandle<IEvent>? _subscriptionHandle;

    public Task OnErrorAsync(Exception ex) => Task.CompletedTask;

    // Stream completion handler
    public async Task OnNextAsync(IEvent item, StreamSequenceToken? token)
    {
        var targetId = item.PartitionKeys.AggregateId;

        // Handle Branch events
        if (item.GetPayload() is BranchCreated || item.GetPayload() is BranchNameChanged)
        {
            var branchEntityWriter = GrainFactory.GetGrain<IBranchEntityPostgresWriter>(item.PartitionKeys.RootPartitionKey);
            var existing = await branchEntityWriter.GetEntityByIdAsync(
                item.PartitionKeys.RootPartitionKey,
                item.PartitionKeys.Group,
                targetId);

            // Create or update branch entity based on event type
            if (item.GetPayload() is BranchCreated created)
            {
                var entity = new BranchDbRecord
                {
                    Id = Guid.NewGuid(),
                    TargetId = targetId,
                    RootPartitionKey = item.PartitionKeys.RootPartitionKey,
                    AggregateGroup = item.PartitionKeys.Group,
                    LastSortableUniqueId = item.SortableUniqueId,
                    TimeStamp = DateTime.UtcNow,
                    Name = created.Name,
                    Country = created.Country // Use Country from the event
                };
                await branchEntityWriter.AddOrUpdateEntityAsync(entity);
            } else if (item.GetPayload() is BranchNameChanged nameChanged && existing != null)
            {
                existing.LastSortableUniqueId = item.SortableUniqueId;
                existing.TimeStamp = DateTime.UtcNow;
                existing.Name = nameChanged.Name;
                // Country property is preserved from existing entity
                await branchEntityWriter.AddOrUpdateEntityAsync(existing);
            }
        }
        // Handle Cart events
        else if (item.GetPayload() is ShoppingCartCreated ||
            item.GetPayload() is ShoppingCartItemAdded ||
            item.GetPayload() is ShoppingCartPaymentProcessed)
        {
            // Handle with in-memory writer
            var cartEntityWriter = GrainFactory.GetGrain<ICartEntityWriter>(item.PartitionKeys.RootPartitionKey);
            var existing = await cartEntityWriter.GetEntityByIdAsync(
                item.PartitionKeys.RootPartitionKey,
                item.PartitionKeys.Group,
                targetId);

            if (item.GetPayload() is ShoppingCartCreated created)
            {
                var entity = new CartEntity
                {
                    Id = Guid.NewGuid(),
                    TargetId = targetId,
                    RootPartitionKey = item.PartitionKeys.RootPartitionKey,
                    AggregateGroup = item.PartitionKeys.Group,
                    LastSortableUniqueId = item.SortableUniqueId,
                    TimeStamp = DateTime.UtcNow,
                    UserId = created.UserId,
                    Items = new List<ShoppingCartItems>(),
                    Status = "Created",
                    TotalAmount = 0
                };
                await cartEntityWriter.AddOrUpdateEntityAsync(entity);
            } else if (item.GetPayload() is ShoppingCartItemAdded itemAdded && existing != null)
            {
                var updatedItems = new List<ShoppingCartItems>(existing.Items)
                {
                    new(itemAdded.Name, itemAdded.Quantity, itemAdded.ItemId, itemAdded.Price)
                };
                var totalAmount = updatedItems.Sum(item => item.Price * item.Quantity);

                var updated = existing with
                {
                    LastSortableUniqueId = item.SortableUniqueId,
                    TimeStamp = DateTime.UtcNow,
                    Items = updatedItems,
                    TotalAmount = totalAmount
                };
                await cartEntityWriter.AddOrUpdateEntityAsync(updated);
            } else if (item.GetPayload() is ShoppingCartPaymentProcessed && existing != null)
            {
                var updated = existing with
                {
                    LastSortableUniqueId = item.SortableUniqueId,
                    TimeStamp = DateTime.UtcNow,
                    Status = "Paid"
                };
                await cartEntityWriter.AddOrUpdateEntityAsync(updated);
            }

            // Handle with Postgres writer
            var cartEntityPostgresWriter = GrainFactory.GetGrain<ICartEntityPostgresWriter>(item.PartitionKeys.RootPartitionKey);
            var existingPostgres = await cartEntityPostgresWriter.GetEntityByIdAsync(
                item.PartitionKeys.RootPartitionKey,
                item.PartitionKeys.Group,
                targetId);

            if (item.GetPayload() is ShoppingCartCreated createdEvent)
            {
                var entity = new CartDbRecord
                {
                    Id = Guid.NewGuid(),
                    TargetId = targetId,
                    RootPartitionKey = item.PartitionKeys.RootPartitionKey,
                    AggregateGroup = item.PartitionKeys.Group,
                    LastSortableUniqueId = item.SortableUniqueId,
                    TimeStamp = DateTime.UtcNow,
                    UserId = createdEvent.UserId,
                    Status = "Created",
                    TotalAmount = 0,
                    ItemsJson = "[]" // Empty array as JSON
                };
                await cartEntityPostgresWriter.AddOrUpdateEntityAsync(entity);
            } 
            else if (item.GetPayload() is ShoppingCartItemAdded itemAddedEvent && existingPostgres != null)
            {
                // Parse existing items from JSON
                List<ShoppingCartItems> items;
                if (string.IsNullOrEmpty(existingPostgres.ItemsJson) || existingPostgres.ItemsJson == "[]")
                {
                    items = new List<ShoppingCartItems>();
                }
                else
                {
                    items = System.Text.Json.JsonSerializer.Deserialize<List<ShoppingCartItems>>(existingPostgres.ItemsJson) ?? new List<ShoppingCartItems>();
                }

                // Add the new item
                items.Add(new ShoppingCartItems(
                    itemAddedEvent.Name, 
                    itemAddedEvent.Quantity, 
                    itemAddedEvent.ItemId, 
                    itemAddedEvent.Price));

                // Calculate total amount
                var totalAmount = items.Sum(i => i.Price * i.Quantity);

                // Update the record
                existingPostgres.LastSortableUniqueId = item.SortableUniqueId;
                existingPostgres.TimeStamp = DateTime.UtcNow;
                existingPostgres.ItemsJson = System.Text.Json.JsonSerializer.Serialize(items);
                existingPostgres.TotalAmount = totalAmount;

                await cartEntityPostgresWriter.AddOrUpdateEntityAsync(existingPostgres);
            } 
            else if (item.GetPayload() is ShoppingCartPaymentProcessed && existingPostgres != null)
            {
                existingPostgres.LastSortableUniqueId = item.SortableUniqueId;
                existingPostgres.TimeStamp = DateTime.UtcNow;
                existingPostgres.Status = "Paid";

                await cartEntityPostgresWriter.AddOrUpdateEntityAsync(existingPostgres);
            }
        }
    }

    public Task OnCompletedAsync() => Task.CompletedTask;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // _logger.LogInformation("OnActivateAsync");

        var streamProvider = this.GetStreamProvider("EventStreamProvider");

        _stream = streamProvider.GetStream<IEvent>(StreamId.Create("AllEvents", Guid.Empty));

        // Subscribe to the stream when this grain is activated
        _subscriptionHandle = await _stream.SubscribeAsync(
            (evt, token) => OnNextAsync(evt, token), // When an event is received
            OnErrorAsync, // When an error occurs
            OnCompletedAsync // When the stream completes
        );

        await base.OnActivateAsync(cancellationToken);
    }
}
