# @sekiban/cosmos

> ⚠️ **Alpha Version**: This package is currently in alpha. APIs may change between releases.

Azure Cosmos DB storage provider for Sekiban Event Sourcing framework.

## Installation

```bash
npm install @sekiban/cosmos@alpha @sekiban/core@alpha
# or
pnpm add @sekiban/cosmos@alpha @sekiban/core@alpha
# or
yarn add @sekiban/cosmos@alpha @sekiban/core@alpha
```

## Features

- Full Azure Cosmos DB support for event storage
- Optimized for global distribution
- Automatic partitioning strategies
- Change feed support for real-time projections
- Multi-region write support
- Consistent indexing policies
- Cost-optimized queries

## Quick Start

```typescript
import { createCosmosStorageProvider } from '@sekiban/cosmos';
import { createSekibanExecutor } from '@sekiban/core';

// Create storage provider
const storageProvider = createCosmosStorageProvider({
  endpoint: 'https://your-cosmos-account.documents.azure.com:443/',
  key: 'your-cosmos-key',
  databaseId: 'sekiban',
  containerId: 'events'
});

// Initialize storage
await storageProvider.initialize();

// Create executor with Cosmos DB storage
const executor = createSekibanExecutor({
  storageProvider,
  domainTypes: yourDomainTypes
});

// Use the executor
const result = await executor.executeCommand(command);
```

## Configuration

```typescript
interface CosmosStorageConfig {
  endpoint: string;         // Cosmos DB endpoint
  key: string;             // Cosmos DB key
  databaseId: string;      // Database name
  containerId?: string;    // Container name (default: 'events')
  throughput?: number;     // RU/s throughput (default: 400)
  partitionKey?: string;   // Partition key path (default: '/partitionKey')
  consistencyLevel?: ConsistencyLevel;  // Consistency level
}
```

## Container Structure

The provider creates containers with optimized partition strategies:

- `events` - Stores all domain events (partitioned by aggregate)
- `snapshots` - Stores aggregate snapshots
- `projections` - Stores multi-projection states

## Best Practices

1. **Partitioning**: Events are automatically partitioned by aggregate ID for optimal performance
2. **Indexing**: Custom indexing policies are applied for efficient queries
3. **Change Feed**: Use change feed for real-time projections
4. **Multi-region**: Configure multi-region writes for global applications

## Requirements

- Azure Cosmos DB account
- Node.js 18 or higher

## License

MIT