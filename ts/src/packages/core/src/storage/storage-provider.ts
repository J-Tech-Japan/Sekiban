import { Result, ResultAsync, ok, err, okAsync, errAsync } from 'neverthrow';
import { IEvent } from '../events/event';
import { IEventReader } from '../events/event-reader';
import { IEventWriter } from '../events/event-writer';
import { EventRetrievalInfo } from '../events/event-retrieval-info';
import { SekibanError } from '../result/errors';
import { InMemoryEventStore } from './in-memory-event-store';

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
  type: StorageProviderType;
  connectionString?: string;
  databaseName?: string;
  containerName?: string;
  tableName?: string;
  maxRetries?: number;
  retryDelayMs?: number;
  timeoutMs?: number;
  enableLogging?: boolean;
}

/**
 * Base storage error
 */
export class StorageError extends SekibanError {
  readonly innerError?: Error;
  
  constructor(
    message: string,
    public readonly code: string,
    innerError?: Error
  ) {
    super(message);
    this.innerError = innerError;
  }
}

/**
 * Connection error
 */
export class ConnectionError extends StorageError {
  constructor(message: string, innerError?: Error) {
    super(message, 'CONNECTION_FAILED', innerError);
  }
}

/**
 * Storage concurrency error with version information
 */
export class StorageConcurrencyError extends StorageError {
  constructor(
    message: string,
    public readonly expectedVersion: number,
    public readonly actualVersion: number
  ) {
    super(message, 'CONCURRENCY_CONFLICT');
  }
}

/**
 * Main event store interface combining reader and writer
 * This replaces the old IEventStorageProvider
 */
export interface IEventStore extends IEventReader, IEventWriter {
  /**
   * Initialize the storage provider
   */
  initialize(): ResultAsync<void, StorageError>;

  /**
   * Close the storage provider
   */
  close(): ResultAsync<void, StorageError>;
}

/**
 * Factory function type for creating event stores
 */
export type EventStoreFactoryFunction = (config: StorageProviderConfig) => ResultAsync<IEventStore, StorageError>;

/**
 * Event store factory
 */
export class EventStoreFactory {
  private static providers = new Map<string, EventStoreFactoryFunction>();

  /**
   * Register an event store factory
   */
  static register(type: string, factory: EventStoreFactoryFunction): void {
    this.providers.set(type, factory);
  }

  /**
   * Check if a provider is registered
   */
  static isRegistered(type: string): boolean {
    return this.providers.has(type);
  }

  /**
   * Create an event store
   */
  static create(config: StorageProviderConfig): ResultAsync<IEventStore, StorageError> {
    const factory = this.providers.get(config.type);
    if (!factory) {
      return errAsync(new StorageError(`Unknown storage provider type: ${config.type}`, 'INVALID_PROVIDER'));
    }
    return factory(config);
  }

  /**
   * Initialize default providers
   */
  static {
    // Register InMemory provider
    this.register(StorageProviderType.InMemory, (config) => {
      return ResultAsync.fromSafePromise(Promise.resolve(new InMemoryEventStore(config)));
    });

    // CosmosDB provider should be registered by the @sekiban/cosmos package

    // PostgreSQL provider should be registered by the @sekiban/postgres package
  }
}

// Export legacy name for backward compatibility
export { EventStoreFactory as StorageProviderFactory };
export type { IEventStore as IEventStorageProvider };