import { describe, it, expect, beforeAll, afterAll } from 'vitest'
import { GenericContainer, StartedTestContainer } from 'testcontainers'
import { PostgresStorageProvider } from './postgres-storage-provider'
import { 
  StorageProviderConfig,
  StorageProviderType,
  EventBatch,
  PartitionKeys,
  IEvent,
  SortableUniqueId
} from '@sekiban/core'

describe('PostgresStorageProvider', () => {
  let container: StartedTestContainer
  let provider: PostgresStorageProvider
  let config: StorageProviderConfig

  beforeAll(async () => {
    // Start PostgreSQL container
    container = await new GenericContainer('postgres:16-alpine')
      .withEnvironment({
        POSTGRES_DB: 'sekiban_test',
        POSTGRES_USER: 'test',
        POSTGRES_PASSWORD: 'test'
      })
      .withExposedPorts(5432)
      .start()

    const host = container.getHost()
    const port = container.getMappedPort(5432)
    
    config = {
      type: StorageProviderType.PostgreSQL,
      connectionString: `postgresql://test:test@${host}:${port}/sekiban_test`,
      maxRetries: 10,
      timeoutMs: 30000
    }

    provider = new PostgresStorageProvider(config)
    
    // Initialize the provider
    const initResult = await provider.initialize()
    if (initResult.isErr()) {
      throw initResult.error
    }
  }, 60000)

  afterAll(async () => {
    await provider.close()
    await container.stop()
  })

  it('should throw error if connection string is missing', () => {
    const invalidConfig: StorageProviderConfig = {
      type: StorageProviderType.PostgreSQL
    }

    expect(() => new PostgresStorageProvider(invalidConfig)).toThrow(
      'Connection string is required for PostgreSQL provider'
    )
  })

  it('should initialize successfully with valid connection', async () => {
    const newProvider = new PostgresStorageProvider(config)
    const result = await newProvider.initialize()
    
    expect(result.isOk()).toBe(true)
    
    await newProvider.close()
  })

  it('should save and load events', async () => {
    const partitionKeys = PartitionKeys.generate('TestAggregate')
    const event: IEvent = {
      sortableUniqueId: SortableUniqueId.generate(new Date()).value,
      eventType: 'TestEvent',
      payload: { message: 'Hello from provider test' },
      aggregateId: partitionKeys.aggregateId,
      partitionKeys,
      version: 1
    }

    const batch: EventBatch = {
      partitionKeys,
      events: [event],
      expectedVersion: 0
    }

    // Save event
    const saveResult = await provider.saveEvents(batch)
    expect(saveResult.isOk()).toBe(true)

    // Load events
    const loadResult = await provider.loadEventsByPartitionKey(partitionKeys)
    expect(loadResult.isOk()).toBe(true)
    
    if (loadResult.isOk()) {
      expect(loadResult.value.length).toBe(1)
      expect(loadResult.value[0].eventType).toBe('TestEvent')
      expect(loadResult.value[0].payload).toEqual({ message: 'Hello from provider test' })
    }
  })

  it('should handle snapshots', async () => {
    const partitionKeys = PartitionKeys.generate('TestAggregate')
    
    // Check no snapshot exists initially
    const getResult = await provider.getLatestSnapshot(partitionKeys)
    expect(getResult.isOk()).toBe(true)
    if (getResult.isOk()) {
      expect(getResult.value).toBe(null)
    }

    // Save snapshot
    const snapshot = {
      partitionKeys,
      version: 10,
      aggregateType: 'TestAggregate',
      payload: { state: 'snapshot data' },
      createdAt: new Date(),
      lastEventId: SortableUniqueId.generate(new Date()).value
    }

    const saveResult = await provider.saveSnapshot(snapshot)
    expect(saveResult.isOk()).toBe(true)

    // Load snapshot
    const loadResult = await provider.getLatestSnapshot(partitionKeys)
    expect(loadResult.isOk()).toBe(true)
    if (loadResult.isOk() && loadResult.value) {
      expect(loadResult.value.version).toBe(10)
      expect(loadResult.value.aggregateType).toBe('TestAggregate')
      expect(loadResult.value.payload).toEqual({ state: 'snapshot data' })
    }
  })

  it('should return error when not initialized', async () => {
    const uninitializedProvider = new PostgresStorageProvider(config)
    const partitionKeys = PartitionKeys.generate('TestAggregate')

    const result = await uninitializedProvider.loadEventsByPartitionKey(partitionKeys)
    expect(result.isErr()).toBe(true)
    if (result.isErr()) {
      expect(result.error.message).toBe('Storage provider not initialized')
    }
  })
})