import { Pool } from 'pg';
import { ResultAsync, okAsync, errAsync } from 'neverthrow';
import {
  IEventStore,
  StorageProviderConfig,
  StorageError,
  ConnectionError,
  EventStoreFactory
} from '@sekiban/core';
import { PostgresEventStore } from './postgres-event-store.js';

/**
 * Creates a PostgreSQL event store
 */
export function createPostgresEventStore(config: StorageProviderConfig): ResultAsync<IEventStore, StorageError> {
  if (!config.connectionString) {
    return errAsync(new StorageError('Connection string is required for PostgreSQL provider', 'INVALID_CONFIG'));
  }

  return ResultAsync.fromPromise(
    (async () => {
      try {
        // Create PostgreSQL pool
        const pool = new Pool({
          connectionString: config.connectionString,
          max: 10, // Maximum number of clients in the pool
          idleTimeoutMillis: 30000, // How long a client is allowed to remain idle
          connectionTimeoutMillis: config.timeoutMs || 2000
        });

        // Test the connection
        const client = await pool.connect();
        client.release();

        // Create event store
        const eventStore = new PostgresEventStore(pool);

        // Initialize the event store
        await eventStore.initialize();

        return eventStore;
      } catch (error) {
        throw new ConnectionError(
          `Failed to create PostgreSQL event store: ${error instanceof Error ? error.message : 'Unknown error'}`,
          error instanceof Error ? error : undefined
        );
      }
    })(),
    (error) => error instanceof StorageError ? error : new ConnectionError(
      `Failed to create PostgreSQL event store: ${error instanceof Error ? error.message : 'Unknown error'}`,
      error instanceof Error ? error : undefined
    )
  );
}

// Register the PostgreSQL provider with the factory
if (typeof EventStoreFactory !== 'undefined') {
  EventStoreFactory.register('PostgreSQL', createPostgresEventStore);
}