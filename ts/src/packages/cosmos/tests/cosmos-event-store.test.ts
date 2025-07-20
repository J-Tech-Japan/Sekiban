import { describe, it, expect, beforeAll, afterAll, beforeEach } from 'vitest'
import { GenericContainer, StartedTestContainer } from 'testcontainers'
import { CosmosClient, Database, Container } from '@azure/cosmos'
import { CosmosEventStore } from './cosmos-event-store'
import { 
  IEvent, 
  PartitionKeys, 
  EventBatch,
  ConcurrencyError,
  SortableUniqueId
} from '@sekiban/core'

describe('CosmosEventStore', () => {
  let container: StartedTestContainer
  let cosmosClient: CosmosClient
  let database: Database
  let eventsContainer: Container
  let snapshotsContainer: Container
  let eventStore: CosmosEventStore

  beforeAll(async () => {
    // Start CosmosDB emulator container
    container = await new GenericContainer('mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest')
      .withEnvironment({
        AZURE_COSMOS_EMULATOR_PARTITION_COUNT: '1',
        AZURE_COSMOS_EMULATOR_ENABLE_DATA_PERSISTENCE: 'false',
        AZURE_COSMOS_EMULATOR_IP_ADDRESS_OVERRIDE: '127.0.0.1'
      })
      .withExposedPorts(8081, 10251, 10252, 10253, 10254)
      .withStartupTimeout(120000)
      .start()

    const host = container.getHost()
    const port = container.getMappedPort(8081)
    
    // Create CosmosDB client
    cosmosClient = new CosmosClient({
      endpoint: `https://${host}:${port}`,
      key: 'C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==', // Default emulator key
      connectionPolicy: {
        requestTimeout: 10000,
        enableEndpointDiscovery: false
      }
    })

    // Initialize database and containers
    const { database: db } = await cosmosClient.databases.createIfNotExists({ id: 'sekiban_test' })
    database = db

    // Initialize event store
    eventStore = new CosmosEventStore(database)
    await eventStore.initialize().match(
      () => {},
      (error) => { throw error }
    )

    eventsContainer = database.container('events')
    snapshotsContainer = database.container('snapshots')
  }, 120000)

  afterAll(async () => {
    await container.stop()
  })

  beforeEach(async () => {
    // Clear containers before each test
    // Note: In real CosmosDB, we would use a different approach
    // For emulator, we can recreate containers
    await eventsContainer.delete()
    await snapshotsContainer.delete()
    
    await eventStore.initialize().match(
      () => {},
      (error) => { throw error }
    )
  })

  describe('initialize', () => {
    it('should create events and snapshots containers', async () => {
      // Get containers
      const { resources: containers } = await database.containers.readAll().fetchAll()
      
      expect(containers.map(c => c.id)).toContain('events')
      expect(containers.map(c => c.id)).toContain('snapshots')
    })

    it('should configure correct partition keys', async () => {
      const eventsResponse = await eventsContainer.read()
      const snapshotsResponse = await snapshotsContainer.read()
      
      expect(eventsResponse.resource?.partitionKey?.paths).toEqual(['/aggregateId'])
      expect(snapshotsResponse.resource?.partitionKey?.paths).toEqual(['/aggregateId'])
    })
  })

  describe('saveEvents', () => {
    it('should save a single event', async () => {
      const partitionKeys = PartitionKeys.generate('TestAggregate')
      const event: IEvent = {
        sortableUniqueId: SortableUniqueId.generate(new Date()).value,
        eventType: 'TestEvent',
        payload: { value: 'test' },
        aggregateId: partitionKeys.aggregateId,
        partitionKeys,
        version: 1
      }

      const batch: EventBatch = {
        partitionKeys,
        events: [event],
        expectedVersion: 0
      }

      const result = await eventStore.saveEvents(batch)
      expect(result.isOk()).toBe(true)

      // Verify event was saved
      const query = {
        query: 'SELECT * FROM c WHERE c.aggregateId = @aggregateId',
        parameters: [{ name: '@aggregateId', value: partitionKeys.aggregateId }]
      }
      const { resources } = await eventsContainer.items.query(query).fetchAll()
      
      expect(resources.length).toBe(1)
      expect(resources[0].seq).toBe(1)
      expect(resources[0].eventType).toBe('TestEvent')
      expect(resources[0].payload).toEqual({ value: 'test' })
    })

    it('should save multiple events using TransactionalBatch', async () => {
      const partitionKeys = PartitionKeys.generate('TestAggregate')
      const events: IEvent[] = [
        {
          sortableUniqueId: SortableUniqueId.generate(new Date()).value,
          eventType: 'TestEvent1',
          payload: { value: 'test1' },
          aggregateId: partitionKeys.aggregateId,
          partitionKeys,
          version: 1
        },
        {
          sortableUniqueId: SortableUniqueId.generate(new Date()).value,
          eventType: 'TestEvent2',
          payload: { value: 'test2' },
          aggregateId: partitionKeys.aggregateId,
          partitionKeys,
          version: 2
        }
      ]

      const batch: EventBatch = {
        partitionKeys,
        events,
        expectedVersion: 0
      }

      const result = await eventStore.saveEvents(batch)
      expect(result.isOk()).toBe(true)

      // Verify events were saved
      const query = {
        query: 'SELECT * FROM c WHERE c.aggregateId = @aggregateId ORDER BY c.seq',
        parameters: [{ name: '@aggregateId', value: partitionKeys.aggregateId }]
      }
      const { resources } = await eventsContainer.items.query(query).fetchAll()
      
      expect(resources.length).toBe(2)
      expect(resources[0].seq).toBe(1)
      expect(resources[0].eventType).toBe('TestEvent1')
      expect(resources[1].seq).toBe(2)
      expect(resources[1].eventType).toBe('TestEvent2')
    })

    it('should detect concurrency conflicts', async () => {
      const partitionKeys = PartitionKeys.generate('TestAggregate')
      
      // Save first event
      const event1: IEvent = {
        sortableUniqueId: SortableUniqueId.generate(new Date()).value,
        eventType: 'TestEvent1',
        payload: { value: 'test1' },
        aggregateId: partitionKeys.aggregateId,
        partitionKeys,
        version: 1
      }

      await eventStore.saveEvents({
        partitionKeys,
        events: [event1],
        expectedVersion: 0
      })

      // Try to save with wrong expected version
      const event2: IEvent = {
        sortableUniqueId: SortableUniqueId.generate(new Date()).value,
        eventType: 'TestEvent2',
        payload: { value: 'test2' },
        aggregateId: partitionKeys.aggregateId,
        partitionKeys,
        version: 1
      }

      const result = await eventStore.saveEvents({
        partitionKeys,
        events: [event2],
        expectedVersion: 0 // Wrong! Should be 1
      })

      expect(result.isErr()).toBe(true)
      if (result.isErr()) {
        expect(result.error).toBeInstanceOf(ConcurrencyError)
      }
    })
  })

  describe('loadEvents', () => {
    it('should load all events for an aggregate', async () => {
      const partitionKeys = PartitionKeys.generate('TestAggregate')
      
      // Save some events
      const events: IEvent[] = [
        {
          sortableUniqueId: SortableUniqueId.generate(new Date()).value,
          eventType: 'TestEvent1',
          payload: { value: 'test1' },
          aggregateId: partitionKeys.aggregateId,
          partitionKeys,
          version: 1
        },
        {
          sortableUniqueId: SortableUniqueId.generate(new Date()).value,
          eventType: 'TestEvent2',
          payload: { value: 'test2' },
          aggregateId: partitionKeys.aggregateId,
          partitionKeys,
          version: 2
        }
      ]

      await eventStore.saveEvents({
        partitionKeys,
        events,
        expectedVersion: 0
      })

      // Load events
      const result = await eventStore.loadEventsByPartitionKey(partitionKeys)
      expect(result.isOk()).toBe(true)
      
      if (result.isOk()) {
        expect(result.value.length).toBe(2)
        expect(result.value[0].eventType).toBe('TestEvent1')
        expect(result.value[1].eventType).toBe('TestEvent2')
      }
    })

    it('should return empty array for non-existent aggregate', async () => {
      const partitionKeys = PartitionKeys.generate('TestAggregate')
      
      const result = await eventStore.loadEventsByPartitionKey(partitionKeys)
      expect(result.isOk()).toBe(true)
      
      if (result.isOk()) {
        expect(result.value).toEqual([])
      }
    })
  })

  describe('hierarchical partition keys', () => {
    it('should use hierarchical partition key for efficient querying', async () => {
      // Test that events are partitioned by aggregate type and ID
      const partitionKeys1 = PartitionKeys.generate('UserAggregate')
      const partitionKeys2 = PartitionKeys.generate('OrderAggregate')
      
      // Save events for different aggregate types
      await eventStore.saveEvents({
        partitionKeys: partitionKeys1,
        events: [{
          sortableUniqueId: SortableUniqueId.generate(new Date()).value,
          eventType: 'UserCreated',
          payload: { type: 'user' },
          aggregateId: partitionKeys1.aggregateId,
          partitionKeys: partitionKeys1,
          version: 1
        }],
        expectedVersion: 0
      })

      await eventStore.saveEvents({
        partitionKeys: partitionKeys2,
        events: [{
          sortableUniqueId: SortableUniqueId.generate(new Date()).value,
          eventType: 'OrderCreated',
          payload: { type: 'order' },
          aggregateId: partitionKeys2.aggregateId,
          partitionKeys: partitionKeys2,
          version: 1
        }],
        expectedVersion: 0
      })

      // Verify events are stored with correct metadata
      const { resources } = await eventsContainer.items.readAll().fetchAll()
      
      const userEvent = resources.find(r => r.eventType === 'UserCreated')
      const orderEvent = resources.find(r => r.eventType === 'OrderCreated')
      
      expect(userEvent).toBeDefined()
      expect(orderEvent).toBeDefined()
      expect(userEvent?.aggregateType).toBe('UserAggregate')
      expect(orderEvent?.aggregateType).toBe('OrderAggregate')
    })
  })
})