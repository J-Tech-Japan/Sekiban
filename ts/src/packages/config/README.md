# @sekiban/config

Configuration management for Sekiban Event Sourcing framework with runtime storage provider selection.

## Installation

```bash
npm install @sekiban/config
```

## Usage

### Loading Configuration from Environment Variables

```typescript
import { loadConfig } from '@sekiban/config'

// Load configuration from process.env
const config = loadConfig()

// Load with custom .env file
const config = loadConfig({ envPath: '.env.production' })

// Load with custom environment variables (useful for testing)
const config = loadConfig({
  env: {
    SEKIBAN_STORAGE_TYPE: 'postgres',
    SEKIBAN_POSTGRES_CONNECTION_STRING: 'postgresql://localhost/test'
  }
})
```

### Configuration Schema

The configuration follows a strongly-typed schema with Zod validation:

```typescript
interface SekibanConfig {
  storage: StorageConfig
  defaultPartitionKey: string
  enableMetrics: boolean
  enableTracing: boolean
  logLevel: 'debug' | 'info' | 'warn' | 'error'
}
```

### Storage Provider Configuration

#### InMemory Storage

```bash
SEKIBAN_STORAGE_TYPE=inmemory
```

#### PostgreSQL Storage

```bash
SEKIBAN_STORAGE_TYPE=postgres
SEKIBAN_POSTGRES_CONNECTION_STRING=postgresql://user:pass@localhost:5432/db
SEKIBAN_POSTGRES_POOL_MIN=2
SEKIBAN_POSTGRES_POOL_MAX=10
```

#### CosmosDB Storage

```bash
SEKIBAN_STORAGE_TYPE=cosmos
SEKIBAN_COSMOS_ENDPOINT=https://account.documents.azure.com:443/
SEKIBAN_COSMOS_KEY=your-key
SEKIBAN_COSMOS_DATABASE_ID=sekiban
SEKIBAN_COSMOS_CONTAINER_ID=events
SEKIBAN_COSMOS_CONSISTENCY_LEVEL=Session
```

### Creating Storage Providers at Runtime

```typescript
import { loadConfig } from '@sekiban/config'
import { createStorageProvider } from '@sekiban/config/runtime'

// Load configuration
const config = loadConfig()

// Create storage provider based on configuration
const storageProvider = await createStorageProvider(config.storage)

// Initialize the provider
await storageProvider.initialize()
```

### Validation

```typescript
import { validateConfig, loadConfigFromJson } from '@sekiban/config'

// Validate configuration object
const isValid = validateConfig(someConfig)

// Load and validate from JSON
try {
  const config = loadConfigFromJson(jsonString)
} catch (error) {
  // Handle validation errors
}
```

### Environment Variables

| Variable | Type | Default | Description |
|----------|------|---------|-------------|
| `SEKIBAN_STORAGE_TYPE` | `inmemory\|postgres\|cosmos` | `inmemory` | Storage provider type |
| `SEKIBAN_DEFAULT_PARTITION_KEY` | string | `default` | Default partition key |
| `SEKIBAN_ENABLE_METRICS` | boolean | `false` | Enable metrics collection |
| `SEKIBAN_ENABLE_TRACING` | boolean | `false` | Enable distributed tracing |
| `SEKIBAN_LOG_LEVEL` | `debug\|info\|warn\|error` | `info` | Logging level |
| `SEKIBAN_MAX_RETRIES` | number | `3` | Maximum retry attempts |
| `SEKIBAN_RETRY_DELAY` | number | `1000` | Retry delay in milliseconds |
| `SEKIBAN_TIMEOUT` | number | `30000` | Operation timeout in milliseconds |

See `.env.example` for a complete example configuration.

## Type-Safe Configuration

All configuration is validated using Zod schemas, providing:

- Runtime validation with detailed error messages
- TypeScript type inference
- Discriminated unions for storage-specific settings
- Automatic transformation of environment variable strings

## Error Handling

```typescript
import { loadConfig, ConfigValidationError } from '@sekiban/config'

try {
  const config = loadConfig()
} catch (error) {
  if (error instanceof ConfigValidationError) {
    console.error('Configuration validation failed:', error.errors)
  }
}

// Or disable throwing errors
const config = loadConfig({ throwOnError: false })
// Returns default configuration on validation errors
```