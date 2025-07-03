import { CosmosClient, Database } from '@azure/cosmos'
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
import { CosmosEventStore } from './cosmos-event-store'

/**
 * CosmosDB storage provider
 */
export class CosmosStorageProvider implements IEventStorageProvider {
  private client: CosmosClient | null = null
  private database: Database | null = null
  private eventStore: CosmosEventStore | null = null

  constructor(private config: StorageProviderConfig) {
    if (!config.connectionString) {
      throw new Error('Connection string is required for CosmosDB provider')
    }
    if (!config.databaseName) {
      throw new Error('Database name is required for CosmosDB provider')
    }
  }

  /**
   * Initialize the storage provider
   */
  initialize(): ResultAsync<void, StorageError> {
    return ResultAsync.fromPromise(
      (async () => {
        try {
          // Parse connection string to extract endpoint and key
          const connectionString = this.config.connectionString!
          const endpoint = this.extractEndpoint(connectionString)
          const key = this.extractKey(connectionString)

          // Create CosmosDB client
          this.client = new CosmosClient({
            endpoint,
            key,
            connectionPolicy: {
              requestTimeout: this.config.timeoutMs || 30000,
              enableEndpointDiscovery: true,
              retryOptions: {
                maxRetryAttemptCount: this.config.maxRetries || 3,
                fixedRetryIntervalInMilliseconds: 1000,
                maxWaitTimeInSeconds: 30
              }
            }
          })

          // Create or get database
          const { database } = await this.client.databases.createIfNotExists({
            id: this.config.databaseName!
          })
          this.database = database

          // Create event store
          this.eventStore = new CosmosEventStore(this.database)

          // Initialize database containers
          return await this.eventStore.initialize()
        } catch (error) {
          throw new ConnectionError(
            `Failed to initialize CosmosDB provider: ${error instanceof Error ? error.message : 'Unknown error'}`,
            error instanceof Error ? error : undefined
          )
        }
      })(),
      (error) => error instanceof StorageError ? error : new ConnectionError(
        `Failed to initialize CosmosDB provider: ${error instanceof Error ? error.message : 'Unknown error'}`,
        error instanceof Error ? error : undefined
      )
    ).andThen(() => okAsync(undefined))
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
  close(): ResultAsync<void, StorageError> {
    if (!this.eventStore) {
      return okAsync(undefined)
    }
    return this.eventStore.close()
  }

  /**
   * Extract endpoint from connection string
   */
  private extractEndpoint(connectionString: string): string {
    const match = connectionString.match(/AccountEndpoint=([^;]+);/)
    if (!match) {
      throw new Error('Invalid connection string: missing AccountEndpoint')
    }
    return match[1]
  }

  /**
   * Extract key from connection string
   */
  private extractKey(connectionString: string): string {
    const match = connectionString.match(/AccountKey=([^;]+);/)
    if (!match) {
      throw new Error('Invalid connection string: missing AccountKey')
    }
    return match[1]
  }
}