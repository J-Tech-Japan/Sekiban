import { Result, ResultAsync, ok, err, okAsync, errAsync } from 'neverthrow'
import { IEvent } from '../events/event.js'
import { PartitionKeys } from '../documents/partition-keys.js'
import { SekibanError } from '../result/errors.js'
import { InMemoryStorageProvider } from './in-memory-storage-provider'
// import { CosmosStorageProvider } from './cosmos-storage-provider'

/**
 * Storage provider types
 */
export enum StorageProviderType {
  InMemory = 'InMemory',
  CosmosDB = 'CosmosDB',
  PostgreSQL = 'PostgreSQL'
}

/**
 * Configuration for storage providers
 */
export interface StorageProviderConfig {
  type: StorageProviderType
  connectionString?: string
  databaseName?: string
  containerName?: string
  tableName?: string
  maxRetries?: number
  retryDelayMs?: number
  timeoutMs?: number
  enableLogging?: boolean
}

/**
 * Batch of events to save
 */
export interface EventBatch {
  partitionKeys: PartitionKeys
  events: IEvent[]
  expectedVersion: number
}

/**
 * Snapshot data structure
 */
export interface SnapshotData {
  partitionKeys: PartitionKeys
  version: number
  aggregateType: string
  payload: any
  createdAt: Date
  lastEventId: string
}

/**
 * Base storage error
 */
export class StorageError extends SekibanError {
  readonly innerError?: Error
  
  constructor(
    message: string,
    public readonly code: string,
    innerError?: Error
  ) {
    super(message)
    this.innerError = innerError
  }
}

/**
 * Connection error
 */
export class ConnectionError extends StorageError {
  constructor(message: string, innerError?: Error) {
    super(message, 'CONNECTION_FAILED', innerError)
  }
}

/**
 * Concurrency error with version information
 */
export class ConcurrencyError extends StorageError {
  constructor(
    message: string,
    public readonly expectedVersion: number,
    public readonly actualVersion: number
  ) {
    super(message, 'CONCURRENCY_CONFLICT')
  }
}

/**
 * Main storage provider interface
 */
export interface IEventStorageProvider {
  /**
   * Save events to storage
   */
  saveEvents(batch: EventBatch): ResultAsync<void, StorageError>

  /**
   * Load all events for a partition key
   */
  loadEventsByPartitionKey(partitionKeys: PartitionKeys): ResultAsync<IEvent[], StorageError>

  /**
   * Load events starting after a specific event ID
   */
  loadEvents(partitionKeys: PartitionKeys, afterEventId?: string): ResultAsync<IEvent[], StorageError>

  /**
   * Get the latest snapshot for an aggregate
   */
  getLatestSnapshot(partitionKeys: PartitionKeys): ResultAsync<SnapshotData | null, StorageError>

  /**
   * Save a snapshot
   */
  saveSnapshot(snapshot: SnapshotData): ResultAsync<void, StorageError>

  /**
   * Initialize the storage provider
   */
  initialize(): ResultAsync<void, StorageError>

  /**
   * Close the storage provider
   */
  close(): ResultAsync<void, StorageError>
}

/**
 * Factory function type for creating storage providers
 */
export type StorageProviderFactoryFunction = (config: StorageProviderConfig) => ResultAsync<IEventStorageProvider, StorageError>

/**
 * Storage provider factory
 */
export class StorageProviderFactory {
  private static providers = new Map<string, StorageProviderFactoryFunction>()

  /**
   * Register a storage provider factory
   */
  static register(type: string, factory: StorageProviderFactoryFunction): void {
    this.providers.set(type, factory)
  }

  /**
   * Check if a provider is registered
   */
  static isRegistered(type: string): boolean {
    return this.providers.has(type)
  }

  /**
   * Create a storage provider
   */
  static create(config: StorageProviderConfig): ResultAsync<IEventStorageProvider, StorageError> {
    const factory = this.providers.get(config.type)
    if (!factory) {
      return errAsync(new StorageError(`Unknown storage provider type: ${config.type}`, 'INVALID_PROVIDER'))
    }
    return factory(config)
  }

  /**
   * Initialize default providers
   */
  static {
    // Register InMemory provider
    this.register(StorageProviderType.InMemory, (config) => {
      return ResultAsync.fromSafePromise(Promise.resolve(new InMemoryStorageProvider(config)))
    })

    // CosmosDB provider should be registered by the @sekiban/cosmos package

    // PostgreSQL provider should be registered by the @sekiban/postgres package
  }
}