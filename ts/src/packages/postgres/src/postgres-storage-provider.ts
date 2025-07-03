import { Pool } from 'pg'
import { ResultAsync, okAsync, errAsync } from 'neverthrow'
import {
  IEventStorageProvider,
  StorageProviderConfig,
  EventBatch,
  SnapshotData,
  StorageError,
  ConnectionError,
  IEvent,
  PartitionKeys
} from '@sekiban/core'
import { PostgresEventStore } from './postgres-event-store-v2'

/**
 * PostgreSQL storage provider
 */
export class PostgresStorageProvider implements IEventStorageProvider {
  private pool: Pool | null = null
  private eventStore: PostgresEventStore | null = null

  constructor(private config: StorageProviderConfig) {
    if (!config.connectionString) {
      throw new Error('Connection string is required for PostgreSQL provider')
    }
  }

  /**
   * Initialize the storage provider
   */
  async initialize(): ResultAsync<void, StorageError> {
    try {
      // Create connection pool
      this.pool = new Pool({
        connectionString: this.config.connectionString,
        max: this.config.maxRetries || 10,
        connectionTimeoutMillis: this.config.timeoutMs || 30000,
        idleTimeoutMillis: 30000,
        allowExitOnIdle: true
      })

      // Test connection
      const client = await this.pool.connect()
      client.release()

      // Create event store
      this.eventStore = new PostgresEventStore(this.pool)

      // Initialize database schema
      return this.eventStore.initialize()
    } catch (error) {
      return errAsync(
        new ConnectionError(
          `Failed to initialize PostgreSQL provider: ${error instanceof Error ? error.message : 'Unknown error'}`,
          error instanceof Error ? error : undefined
        )
      )
    }
  }

  /**
   * Save events to storage
   */
  saveEvents(batch: EventBatch): ResultAsync<void, StorageError> {
    if (!this.eventStore) {
      return errAsync(new ConnectionError('Storage provider not initialized'))
    }
    return this.eventStore.saveEvents(batch)
  }

  /**
   * Load all events for a partition key
   */
  loadEventsByPartitionKey(partitionKeys: PartitionKeys): ResultAsync<IEvent[], StorageError> {
    if (!this.eventStore) {
      return errAsync(new ConnectionError('Storage provider not initialized'))
    }
    return this.eventStore.loadEventsByPartitionKey(partitionKeys)
  }

  /**
   * Load events starting after a specific event ID
   */
  loadEvents(partitionKeys: PartitionKeys, afterEventId?: string): ResultAsync<IEvent[], StorageError> {
    if (!this.eventStore) {
      return errAsync(new ConnectionError('Storage provider not initialized'))
    }
    return this.eventStore.loadEvents(partitionKeys, afterEventId)
  }

  /**
   * Get the latest snapshot for an aggregate
   */
  getLatestSnapshot(partitionKeys: PartitionKeys): ResultAsync<SnapshotData | null, StorageError> {
    if (!this.eventStore) {
      return errAsync(new ConnectionError('Storage provider not initialized'))
    }
    return this.eventStore.getLatestSnapshot(partitionKeys)
  }

  /**
   * Save a snapshot
   */
  saveSnapshot(snapshot: SnapshotData): ResultAsync<void, StorageError> {
    if (!this.eventStore) {
      return errAsync(new ConnectionError('Storage provider not initialized'))
    }
    return this.eventStore.saveSnapshot(snapshot)
  }

  /**
   * Close the storage provider
   */
  async close(): ResultAsync<void, StorageError> {
    if (!this.eventStore) {
      return okAsync(undefined)
    }
    return this.eventStore.close()
  }
}