import { describe, it, expect } from 'vitest'
import { Result, ResultAsync, ok, err } from 'neverthrow'
import {
  IEventStorageProvider,
  StorageProviderConfig,
  EventBatch,
  SnapshotData,
  StorageError,
  ConnectionError,
  ConcurrencyError,
  StorageProviderFactory,
  StorageProviderType
} from './storage-provider'
import { IEvent } from '../events/event'
import { PartitionKeys } from '../documents/partition-keys'
import { SortableUniqueId } from '../documents/sortable-unique-id'
import { UserCreated } from '../test-helpers/test-events'
import { createEvent } from '../events/event'

describe('Storage Provider Interfaces', () => {
  describe('IEventStorageProvider', () => {
    it('should define saveEvents method', () => {
      // Arrange
      const provider: IEventStorageProvider = {
        saveEvents: async () => ok(undefined),
        loadEvents: async () => ok([]),
        loadEventsByPartitionKey: async () => ok([]),
        getLatestSnapshot: async () => ok(null),
        saveSnapshot: async () => ok(undefined),
        initialize: async () => ok(undefined),
        close: async () => ok(undefined)
      }
      
      // Act & Assert
      expect(typeof provider.saveEvents).toBe('function')
    })

    it('should define loadEvents method', () => {
      // Arrange
      const provider: IEventStorageProvider = {
        saveEvents: async () => ok(undefined),
        loadEvents: async () => ok([]),
        loadEventsByPartitionKey: async () => ok([]),
        getLatestSnapshot: async () => ok(null),
        saveSnapshot: async () => ok(undefined),
        initialize: async () => ok(undefined),
        close: async () => ok(undefined)
      }
      
      // Act & Assert
      expect(typeof provider.loadEvents).toBe('function')
    })

    it('should define loadEventsByPartitionKey method', () => {
      // Arrange
      const provider: IEventStorageProvider = {
        saveEvents: async () => ok(undefined),
        loadEvents: async () => ok([]),
        loadEventsByPartitionKey: async () => ok([]),
        getLatestSnapshot: async () => ok(null),
        saveSnapshot: async () => ok(undefined),
        initialize: async () => ok(undefined),
        close: async () => ok(undefined)
      }
      
      // Act & Assert
      expect(typeof provider.loadEventsByPartitionKey).toBe('function')
    })

    it('should define snapshot methods', () => {
      // Arrange
      const provider: IEventStorageProvider = {
        saveEvents: async () => ok(undefined),
        loadEvents: async () => ok([]),
        loadEventsByPartitionKey: async () => ok([]),
        getLatestSnapshot: async () => ok(null),
        saveSnapshot: async () => ok(undefined),
        initialize: async () => ok(undefined),
        close: async () => ok(undefined)
      }
      
      // Act & Assert
      expect(typeof provider.getLatestSnapshot).toBe('function')
      expect(typeof provider.saveSnapshot).toBe('function')
    })

    it('should define lifecycle methods', () => {
      // Arrange
      const provider: IEventStorageProvider = {
        saveEvents: async () => ok(undefined),
        loadEvents: async () => ok([]),
        loadEventsByPartitionKey: async () => ok([]),
        getLatestSnapshot: async () => ok(null),
        saveSnapshot: async () => ok(undefined),
        initialize: async () => ok(undefined),
        close: async () => ok(undefined)
      }
      
      // Act & Assert
      expect(typeof provider.initialize).toBe('function')
      expect(typeof provider.close).toBe('function')
    })
  })

  describe('StorageProviderConfig', () => {
    it('should create config with all properties', () => {
      // Arrange & Act
      const config: StorageProviderConfig = {
        type: StorageProviderType.InMemory,
        connectionString: 'connection-string',
        databaseName: 'test-db',
        containerName: 'events',
        maxRetries: 3,
        retryDelayMs: 100,
        timeoutMs: 5000,
        enableLogging: true
      }
      
      // Assert
      expect(config.type).toBe(StorageProviderType.InMemory)
      expect(config.connectionString).toBe('connection-string')
      expect(config.databaseName).toBe('test-db')
      expect(config.containerName).toBe('events')
      expect(config.maxRetries).toBe(3)
      expect(config.retryDelayMs).toBe(100)
      expect(config.timeoutMs).toBe(5000)
      expect(config.enableLogging).toBe(true)
    })

    it('should create config with minimal properties', () => {
      // Arrange & Act
      const config: StorageProviderConfig = {
        type: StorageProviderType.InMemory
      }
      
      // Assert
      expect(config.type).toBe(StorageProviderType.InMemory)
      expect(config.connectionString).toBeUndefined()
      expect(config.maxRetries).toBeUndefined()
    })
  })

  describe('EventBatch', () => {
    it('should create event batch', () => {
      // Arrange
      const partitionKeys = PartitionKeys.generate('users')
      const event1 = createEvent({
        partitionKeys,
        aggregateType: 'User',
        version: 1,
        payload: new UserCreated('John', 'john@example.com')
      })
      const event2 = createEvent({
        partitionKeys,
        aggregateType: 'User',
        version: 2,
        payload: new UserCreated('Jane', 'jane@example.com')
      })
      
      // Act
      const batch: EventBatch = {
        partitionKeys,
        events: [event1, event2],
        expectedVersion: 0
      }
      
      // Assert
      expect(batch.partitionKeys).toBe(partitionKeys)
      expect(batch.events).toHaveLength(2)
      expect(batch.expectedVersion).toBe(0)
    })
  })

  describe('SnapshotData', () => {
    it('should create snapshot data', () => {
      // Arrange
      const partitionKeys = PartitionKeys.generate('users')
      const aggregateData = { name: 'John', email: 'john@example.com' }
      
      // Act
      const snapshot: SnapshotData = {
        partitionKeys,
        version: 10,
        aggregateType: 'User',
        payload: aggregateData,
        createdAt: new Date('2024-01-01'),
        lastEventId: SortableUniqueId.generate().toString()
      }
      
      // Assert
      expect(snapshot.partitionKeys).toBe(partitionKeys)
      expect(snapshot.version).toBe(10)
      expect(snapshot.aggregateType).toBe('User')
      expect(snapshot.payload).toEqual(aggregateData)
      expect(snapshot.createdAt).toEqual(new Date('2024-01-01'))
      expect(snapshot.lastEventId).toBeDefined()
    })
  })

  describe('Storage Errors', () => {
    it('should create StorageError', () => {
      // Arrange & Act
      const error = new StorageError('Failed to save events', 'SAVE_FAILED')
      
      // Assert
      expect(error).toBeInstanceOf(StorageError)
      expect(error.message).toBe('Failed to save events')
      expect(error.code).toBe('SAVE_FAILED')
      expect(error.name).toBe('StorageError')
    })

    it('should create ConnectionError', () => {
      // Arrange & Act
      const error = new ConnectionError('Database connection failed')
      
      // Assert
      expect(error).toBeInstanceOf(ConnectionError)
      expect(error).toBeInstanceOf(StorageError)
      expect(error.message).toBe('Database connection failed')
      expect(error.code).toBe('CONNECTION_FAILED')
    })

    it('should create ConcurrencyError', () => {
      // Arrange & Act
      const error = new ConcurrencyError('Version conflict', 5, 3)
      
      // Assert
      expect(error).toBeInstanceOf(ConcurrencyError)
      expect(error).toBeInstanceOf(StorageError)
      expect(error.message).toBe('Version conflict')
      expect(error.code).toBe('CONCURRENCY_CONFLICT')
      expect(error.expectedVersion).toBe(5)
      expect(error.actualVersion).toBe(3)
    })
  })

  describe('StorageProviderFactory', () => {
    it('should create InMemory provider', async () => {
      // Arrange
      const config: StorageProviderConfig = {
        type: StorageProviderType.InMemory
      }
      
      // Act
      const result = await StorageProviderFactory.create(config)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const provider = result._unsafeUnwrap()
      expect(provider).toBeDefined()
    })

    it('should create CosmosDB provider', async () => {
      // Arrange
      const config: StorageProviderConfig = {
        type: StorageProviderType.CosmosDB,
        connectionString: 'AccountEndpoint=https://localhost:8081/;AccountKey=test',
        databaseName: 'test-db',
        containerName: 'events'
      }
      
      // Act
      const result = await StorageProviderFactory.create(config)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const provider = result._unsafeUnwrap()
      expect(provider).toBeDefined()
    })

    it('should create PostgreSQL provider', async () => {
      // Arrange
      const config: StorageProviderConfig = {
        type: StorageProviderType.PostgreSQL,
        connectionString: 'postgres://user:pass@localhost:5432/testdb',
        databaseName: 'testdb'
      }
      
      // Act
      const result = await StorageProviderFactory.create(config)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const provider = result._unsafeUnwrap()
      expect(provider).toBeDefined()
    })

    it('should return error for invalid config', async () => {
      // Arrange
      const config: StorageProviderConfig = {
        type: 'InvalidType' as any
      }
      
      // Act
      const result = await StorageProviderFactory.create(config)
      
      // Assert
      expect(result.isErr()).toBe(true)
      const error = result._unsafeUnwrapErr()
      expect(error).toBeInstanceOf(StorageError)
    })

    it('should register custom provider', () => {
      // Arrange
      const customProvider: IEventStorageProvider = {
        saveEvents: async () => ok(undefined),
        loadEvents: async () => ok([]),
        loadEventsByPartitionKey: async () => ok([]),
        getLatestSnapshot: async () => ok(null),
        saveSnapshot: async () => ok(undefined),
        initialize: async () => ok(undefined),
        close: async () => ok(undefined)
      }
      
      // Act
      StorageProviderFactory.register('custom', () => ResultAsync.fromSafePromise(Promise.resolve(customProvider)))
      
      // Assert
      expect(StorageProviderFactory.isRegistered('custom')).toBe(true)
    })
  })
})

describe('Storage Provider Implementation Tests', () => {
  describe('InMemoryStorageProvider', () => {
    let provider: IEventStorageProvider

    beforeEach(async () => {
      const config: StorageProviderConfig = {
        type: StorageProviderType.InMemory
      }
      const result = await StorageProviderFactory.create(config)
      provider = result._unsafeUnwrap()
      await provider.initialize()
    })

    afterEach(async () => {
      await provider.close()
    })

    it('should save and load events', async () => {
      // Arrange
      const partitionKeys = PartitionKeys.generate('users')
      const event = createEvent({
        partitionKeys,
        aggregateType: 'User',
        version: 1,
        payload: new UserCreated('John', 'john@example.com')
      })
      const batch: EventBatch = {
        partitionKeys,
        events: [event],
        expectedVersion: 0
      }
      
      // Act
      const saveResult = await provider.saveEvents(batch)
      const loadResult = await provider.loadEventsByPartitionKey(partitionKeys)
      
      // Assert
      expect(saveResult.isOk()).toBe(true)
      expect(loadResult.isOk()).toBe(true)
      const events = loadResult._unsafeUnwrap()
      expect(events).toHaveLength(1)
      expect(events[0].payload).toEqual(event.payload)
    })

    it('should handle concurrent writes with version check', async () => {
      // Arrange
      const partitionKeys = PartitionKeys.generate('users')
      const event1 = createEvent({
        partitionKeys,
        aggregateType: 'User',
        version: 1,
        payload: new UserCreated('John', 'john@example.com')
      })
      const event2 = createEvent({
        partitionKeys,
        aggregateType: 'User',
        version: 1,
        payload: new UserCreated('Jane', 'jane@example.com')
      })
      
      // Act
      const batch1: EventBatch = { partitionKeys, events: [event1], expectedVersion: 0 }
      const batch2: EventBatch = { partitionKeys, events: [event2], expectedVersion: 0 }
      
      const saveResult1 = await provider.saveEvents(batch1)
      const saveResult2 = await provider.saveEvents(batch2)
      
      // Assert
      expect(saveResult1.isOk()).toBe(true)
      expect(saveResult2.isErr()).toBe(true)
      const error = saveResult2._unsafeUnwrapErr()
      expect(error).toBeInstanceOf(ConcurrencyError)
    })

    it('should save and load snapshots', async () => {
      // Arrange
      const partitionKeys = PartitionKeys.generate('users')
      const snapshot: SnapshotData = {
        partitionKeys,
        version: 10,
        aggregateType: 'User',
        payload: { name: 'John', email: 'john@example.com' },
        createdAt: new Date(),
        lastEventId: SortableUniqueId.generate().toString()
      }
      
      // Act
      const saveResult = await provider.saveSnapshot(snapshot)
      const loadResult = await provider.getLatestSnapshot(partitionKeys)
      
      // Assert
      expect(saveResult.isOk()).toBe(true)
      expect(loadResult.isOk()).toBe(true)
      const loaded = loadResult._unsafeUnwrap()
      expect(loaded).not.toBeNull()
      expect(loaded?.version).toBe(10)
      expect(loaded?.payload).toEqual(snapshot.payload)
    })

    it('should load events after snapshot', async () => {
      // Arrange
      const partitionKeys = PartitionKeys.generate('users')
      
      // Save initial events
      const events1to5 = Array.from({ length: 5 }, (_, i) => 
        createEvent({
          partitionKeys,
          aggregateType: 'User',
          version: i + 1,
          payload: new UserCreated(`User${i}`, `user${i}@example.com`)
        })
      )
      await provider.saveEvents({ partitionKeys, events: events1to5, expectedVersion: 0 })
      
      // Save snapshot at version 5
      const snapshot: SnapshotData = {
        partitionKeys,
        version: 5,
        aggregateType: 'User',
        payload: { count: 5 },
        createdAt: new Date(),
        lastEventId: events1to5[4].id
      }
      await provider.saveSnapshot(snapshot)
      
      // Save more events
      const events6to8 = Array.from({ length: 3 }, (_, i) => 
        createEvent({
          partitionKeys,
          aggregateType: 'User',
          version: i + 6,
          payload: new UserCreated(`User${i + 5}`, `user${i + 5}@example.com`)
        })
      )
      await provider.saveEvents({ partitionKeys, events: events6to8, expectedVersion: 5 })
      
      // Act
      const result = await provider.loadEvents(partitionKeys, snapshot.lastEventId)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const events = result._unsafeUnwrap()
      expect(events).toHaveLength(3)
      expect(events[0].version).toBe(6)
    })

    it('should handle loading non-existent aggregate', async () => {
      // Arrange
      const partitionKeys = PartitionKeys.existing('non-existent', 'users')
      
      // Act
      const result = await provider.loadEventsByPartitionKey(partitionKeys)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const events = result._unsafeUnwrap()
      expect(events).toHaveLength(0)
    })
  })
})