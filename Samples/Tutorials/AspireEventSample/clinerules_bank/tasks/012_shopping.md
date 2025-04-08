I want to improve 
AspireEventSample.ReadModels/BranchDbContext.cs

AspireEventSample.ReadModels/CartDbRecord.cs
has items as in json but it is not what i want.
I want to make 
CartItemDbRecord
and save it to the different table
Put cart id to the CartItemDbRecord
IReadModelEntity should be implemented so we can make separate readmodel class.

First just read necessary document and write you plan so I can review.

Append your plan to 
clinerules_bank/tasks/012_shopping.md 
-----------

## Implementation Plan for CartItemDbRecord

After reviewing the current implementation, here's my plan to implement the CartItemDbRecord:

### 1. Create CartItemDbRecord Class
Create a new class in AspireEventSample.ReadModels that implements IReadModelEntity:

```csharp
using Orleans.Serialization;
using Sekiban.Pure.Orleans.ReadModels;

namespace AspireEventSample.ReadModels;

[GenerateSerializer]
public class CartItemDbRecord : IReadModelEntity
{
    // IReadModelEntity properties
    [Id(0)]
    public Guid Id { get; set; }
    [Id(1)]
    public Guid TargetId { get; set; }
    [Id(2)]
    public string RootPartitionKey { get; set; } = string.Empty;
    [Id(3)]
    public string AggregateGroup { get; set; } = string.Empty;
    [Id(4)]
    public string LastSortableUniqueId { get; set; } = string.Empty;
    [Id(5)]
    public DateTime TimeStamp { get; set; }

    // Cart item specific properties
    [Id(6)]
    public Guid CartId { get; set; } // Reference to the parent cart
    [Id(7)]
    public string Name { get; set; } = string.Empty;
    [Id(8)]
    public int Quantity { get; set; }
    [Id(9)]
    public Guid ItemId { get; set; }
    [Id(10)]
    public int Price { get; set; }
}
```

### 2. Update BranchDbContext
Add a DbSet for CartItemDbRecord and configure it in OnModelCreating:

```csharp
public DbSet<CartItemDbRecord> CartItems { get; set; } = null!;

// In OnModelCreating:
modelBuilder.Entity<CartItemDbRecord>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Id).ValueGeneratedNever();
    entity.Property(e => e.TargetId).IsRequired();
    entity.Property(e => e.RootPartitionKey).IsRequired();
    entity.Property(e => e.AggregateGroup).IsRequired();
    entity.Property(e => e.LastSortableUniqueId).IsRequired();
    entity.Property(e => e.TimeStamp).IsRequired();
    entity.Property(e => e.CartId).IsRequired();
    entity.Property(e => e.Name).IsRequired();
    entity.Property(e => e.Quantity).IsRequired();
    entity.Property(e => e.ItemId).IsRequired();
    entity.Property(e => e.Price).IsRequired();

    // Create indexes for faster lookups
    entity.HasIndex(e => new { e.RootPartitionKey, e.AggregateGroup, e.TargetId });
    entity.HasIndex(e => e.CartId); // Index for cart ID lookups
});
```

### 3. Update CartDbRecord
Remove the ItemsJson property from CartDbRecord since items will now be stored in a separate table.

### 4. Create ICartItemEntityPostgresWriter Interface
Create a new interface for accessing cart items:

```csharp
public interface ICartItemEntityPostgresWriter : IReadModelAccessor<CartItemDbRecord>, IGrainWithStringKey
{
    Task<List<CartItemDbRecord>> GetItemsByCartIdAsync(Guid cartId);
}
```

### 5. Implement CartItemEntityPostgresWriter
Create a new implementation of the interface:

```csharp
public class CartItemEntityPostgresWriter : ICartItemEntityPostgresWriter
{
    private readonly BranchDbContext _dbContext;

    public CartItemEntityPostgresWriter(BranchDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // Implement IReadModelAccessor methods...

    public async Task<List<CartItemDbRecord>> GetItemsByCartIdAsync(Guid cartId)
    {
        return await _dbContext.CartItems
            .Where(item => item.CartId == cartId)
            .ToListAsync();
    }
}
```

### 6. Update ShoppingCartReadModelHandler
Modify the handler to work with the new cart item entity:

- For ShoppingCartCreated: No changes needed as it doesn't involve items
- For ShoppingCartItemAdded: Create a new CartItemDbRecord instead of updating ItemsJson
- For ShoppingCartPaymentProcessed: No changes needed as it doesn't involve items

### 7. Create Migration
Create a new migration to update the database schema:

```bash
dotnet ef migrations add AddCartItemTable --project AspireEventSample.MigrationHost
```

### 8. Update Tests
Update any tests that rely on the current implementation to work with the new structure.

This approach will:
1. Store cart items in a separate table
2. Maintain the relationship between carts and items through the CartId property
3. Allow for more efficient querying of cart items
4. Eliminate the need for JSON serialization/deserialization
