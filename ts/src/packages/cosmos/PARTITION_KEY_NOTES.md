# Cosmos DB Partition Key Configuration

## Hierarchical Partition Key Structure

The Cosmos DB implementation uses a hierarchical partition key with three levels:

1. `/rootPartitionKey` - Tenant or root-level partitioning (default: "default")
2. `/aggregateGroup` - Aggregate type or group (e.g., "WeatherForecastProjector")
3. `/partitionKey` - Full partition key string (format: `rootPartitionKey@aggregateGroup@aggregateId`)

## Example Document Structure

```json
{
  "id": "01981ca8-31fc-7841-b82c-f4c6cabb8fba",
  "rootPartitionKey": "default",
  "aggregateGroup": "WeatherForecastProjector",
  "partitionKey": "default@WeatherForecastProjector@01981ca8-31fc-7841-b82c-f4c6cabb8fba",
  // ... other fields
}
```

## Container Configuration

```typescript
{
  id: 'events',
  partitionKey: { 
    paths: ['/rootPartitionKey', '/aggregateGroup', '/partitionKey'],
    kind: 'MultiHash'
  }
}
```

This configuration enables:
- Efficient queries by tenant (rootPartitionKey)
- Queries by aggregate type within a tenant
- Direct access to specific aggregates using the full partition key

## Compatibility with C# Implementation

This structure exactly matches the C# Sekiban implementation as defined in:
`CosmosDbFactory.cs` line 269-270