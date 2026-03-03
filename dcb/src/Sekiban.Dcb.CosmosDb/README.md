# Sekiban.Dcb.CosmosDb

Azure Cosmos DB storage implementation for Sekiban DCB Event Sourcing Framework.

## Features

- Event storage in Cosmos DB
- Tag-based event retrieval
- Automatic container creation
- Optimized partitioning strategy

## Database Structure

### Database
- Default name: `SekibanDcb`

### Containers

#### Events Container
- Name: `events`
- Partition Key: `/pk`
- Stores all events with their metadata

#### Tags Container
- Name: `tags`
- Partition Key: `/pk`
- Stores tag-to-event mappings for efficient tag-based queries

#### Multi Projection States Container
- Name: `multiProjectionStates`
- Partition Key: `/pk`
- Stores multi-projection snapshots and metadata

## Configuration

### Using appsettings.json

```json
{
  "ConnectionStrings": {
    "SekibanDcbCosmos": "AccountEndpoint=https://localhost:8081/;AccountKey=..."
  },
  "CosmosDb": {
    "DatabaseName": "SekibanDcb"
  },
  "Sekiban": {
    "Database": "cosmos"
  }
}
```

The system will look for connection strings in the following order:
1. `ConnectionStrings:SekibanDcbCosmos` (most specific)
2. `ConnectionStrings:SekibanDcbCosmosDb`
3. `ConnectionStrings:CosmosDb`
4. `ConnectionStrings:cosmosdb` (most general)

### Using Dependency Injection

```csharp
// With configuration
services.AddSekibanDcbCosmosDb(configuration);

// With connection string
services.AddSekibanDcbCosmosDb(connectionString, "SekibanDcb");

// For Aspire projects (automatically uses Aspire's CosmosClient if available)
services.AddSekibanDcbCosmosDbWithAspire();
```

### Aspire Integration

When using with Aspire, the CosmosDB support will automatically detect and use the CosmosClient registered by Aspire. If no Aspire client is available, it will fall back to using a connection string from configuration.

```csharp
// In Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add Aspire services (this may register CosmosClient automatically based on Aspire configuration)
builder.AddServiceDefaults();

// Configure database storage based on configuration
var databaseType = builder.Configuration.GetSection("Sekiban").GetValue<string>("Database")?.ToLower();

if (databaseType == "cosmos")
{
    // This will automatically use Aspire's CosmosClient if available,
    // or fall back to connection string from configuration
    builder.Services.AddSekibanDcbCosmosDbWithAspire();
}
else
{
    // Use Postgres or other storage (default)
    builder.Services.AddSekibanDcbPostgresWithAspire();
}
```

The system will automatically:
1. First try to use CosmosClient from Aspire DI container
2. If not available, look for connection string in order:
   - `ConnectionStrings:SekibanDcbCosmos`
   - `ConnectionStrings:SekibanDcbCosmosDb`
   - `ConnectionStrings:CosmosDb`
   - `ConnectionStrings:cosmosdb`
3. Use the configured database name from `CosmosDb:DatabaseName` (defaults to "SekibanDcb")

## Partitioning Strategy

- **Events Container**: `pk = {serviceId}|{eventId}`
- **Tags Container**: `pk = {serviceId}|{tag}`
- **States Container**: `pk = {serviceId}|MultiProjectionState_{projectorName}`

Runtime compatibility for legacy Cosmos schemas has been removed. Migrate existing data with `tools/MigrateDcbCosmosEventsTags` before deploying this version.

## Query Patterns

1. **Read all events**: Query events container ordered by SortableUniqueId
2. **Read events by tag**: 
   - First query tags container to get event IDs
   - Then batch retrieve events from events container
3. **Get latest tag state**: Query tags container for the most recent entry

## Performance Considerations

- Uses autoscale throughput (starting at 1000 RU/s)
- Bulk operations enabled for better write performance
- Optimized for write-heavy workloads typical in event sourcing

## Requirements

- .NET 9.0 or later
- Azure Cosmos DB account (or local emulator for development)
- Microsoft.Azure.Cosmos 3.46.0 or later
