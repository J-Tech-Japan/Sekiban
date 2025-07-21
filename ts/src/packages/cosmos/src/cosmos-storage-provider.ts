import { CosmosClient, Database } from '@azure/cosmos';
import { ResultAsync, okAsync, errAsync } from 'neverthrow';
import {
  IEventStore,
  StorageProviderConfig,
  StorageError,
  ConnectionError,
  EventStoreFactory,
  SekibanDomainTypes,
  SchemaRegistry
} from '@sekiban/core';
import { CosmosEventStore } from './cosmos-event-store';

/**
 * Extended configuration for CosmosDB that includes domain types
 */
export interface CosmosStorageProviderConfig extends StorageProviderConfig {
  domainTypes?: SekibanDomainTypes;
  registry?: SchemaRegistry;
}

/**
 * Creates a CosmosDB event store
 */
export function createCosmosEventStore(config: CosmosStorageProviderConfig): ResultAsync<IEventStore, StorageError> {
  if (!config.connectionString) {
    return errAsync(new StorageError('Connection string is required for CosmosDB provider', 'INVALID_CONFIG'));
  }
  if (!config.databaseName) {
    return errAsync(new StorageError('Database name is required for CosmosDB provider', 'INVALID_CONFIG'));
  }

  return ResultAsync.fromPromise(
    (async () => {
      try {
        // Parse connection string to extract endpoint and key
        const connectionString = config.connectionString!; // We already checked this above
        const endpoint = extractEndpoint(connectionString);
        const key = extractKey(connectionString);

        // Create CosmosDB client
        const client = new CosmosClient({
          endpoint,
          key,
          connectionPolicy: {
            requestTimeout: config.timeoutMs || 30000,
            enableEndpointDiscovery: true,
            retryOptions: {
              maxRetryAttemptCount: config.maxRetries || 3,
              fixedRetryIntervalInMilliseconds: 1000,
              maxWaitTimeInSeconds: 30
            }
          }
        });

        // Create or get database
        const { database } = await client.databases.createIfNotExists({
          id: config.databaseName
        });

        // Create event store with optional domain types and registry
        const eventStore = new CosmosEventStore(database, config.domainTypes, config.registry);

        // Initialize the event store
        await eventStore.initialize();

        return eventStore;
      } catch (error) {
        throw new ConnectionError(
          `Failed to create CosmosDB event store: ${error instanceof Error ? error.message : 'Unknown error'}`,
          error instanceof Error ? error : undefined
        );
      }
    })(),
    (error) => error instanceof StorageError ? error : new ConnectionError(
      `Failed to create CosmosDB event store: ${error instanceof Error ? error.message : 'Unknown error'}`,
      error instanceof Error ? error : undefined
    )
  );
}

/**
 * Extract endpoint from connection string
 */
function extractEndpoint(connectionString: string): string {
  const match = connectionString.match(/AccountEndpoint=([^;]+);/);
  if (!match || !match[1]) {
    throw new Error('Invalid connection string: missing AccountEndpoint');
  }
  return match[1];
}

/**
 * Extract key from connection string
 */
function extractKey(connectionString: string): string {
  const match = connectionString.match(/AccountKey=([^;]+);/);
  if (!match || !match[1]) {
    throw new Error('Invalid connection string: missing AccountKey');
  }
  return match[1];
}

// Register the CosmosDB provider with the factory
if (typeof EventStoreFactory !== 'undefined') {
  EventStoreFactory.register('CosmosDB', (config) => createCosmosEventStore(config as CosmosStorageProviderConfig));
}