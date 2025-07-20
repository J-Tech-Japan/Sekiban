# @sekiban/postgres

> ⚠️ **Alpha Version**: This package is currently in alpha. APIs may change between releases.

PostgreSQL storage provider for Sekiban Event Sourcing framework.

## Installation

```bash
npm install @sekiban/postgres@alpha @sekiban/core@alpha
# or
pnpm add @sekiban/postgres@alpha @sekiban/core@alpha
# or
yarn add @sekiban/postgres@alpha @sekiban/core@alpha
```

## Features

- Full PostgreSQL support for event storage
- Optimistic concurrency control
- Event streaming capabilities
- Snapshot support
- Multi-tenant support with partition keys
- Connection pooling with pg-pool

## Quick Start

```typescript
import { createPostgresStorageProvider } from '@sekiban/postgres';
import { createSekibanExecutor } from '@sekiban/core';

// Create storage provider
const storageProvider = createPostgresStorageProvider({
  connectionString: 'postgresql://user:pass@localhost:5432/sekiban',
  maxRetries: 3,
  retryDelayMs: 100
});

// Initialize storage
await storageProvider.initialize();

// Create executor with PostgreSQL storage
const executor = createSekibanExecutor({
  storageProvider,
  domainTypes: yourDomainTypes
});

// Use the executor
const result = await executor.executeCommand(command);
```

## Configuration

```typescript
interface PostgresStorageConfig {
  connectionString: string;  // PostgreSQL connection string
  poolSize?: number;        // Connection pool size (default: 10)
  maxRetries?: number;      // Max retry attempts (default: 3)
  retryDelayMs?: number;    // Delay between retries (default: 100ms)
  schema?: string;          // Database schema (default: 'public')
}
```

## Database Schema

The provider will automatically create the required tables:

- `events` - Stores all domain events
- `snapshots` - Stores aggregate snapshots
- `projections` - Stores multi-projection states

## Requirements

- PostgreSQL 12 or higher
- Node.js 18 or higher

## License

MIT