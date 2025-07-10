import { describe, it, expect, vi, beforeEach } from 'vitest'
import { CosmosEventStore } from './cosmos-event-store'
import { Database, Container } from '@azure/cosmos'
import { 
  PartitionKeys,
  EventBatch,
  IEvent,
  SortableUniqueId
} from '@sekiban/core'

// Mock the CosmosDB SDK
vi.mock('@azure/cosmos', () => {
  const mockContainer = {
    items: {
      create: vi.fn().mockResolvedValue({}),
      query: vi.fn().mockReturnValue({
        fetchAll: vi.fn().mockResolvedValue({ resources: [] })
      }),
      batch: vi.fn()
    },
    item: vi.fn().mockReturnValue({
      read: vi.fn().mockResolvedValue({ resource: null })
    }),
    read: vi.fn().mockResolvedValue({
      resource: {
        partitionKey: {
          paths: ['/aggregateId']
        }
      }
    })
  }

  const mockDatabase = {
    containers: {
      createIfNotExists: vi.fn().mockResolvedValue({
        container: mockContainer
      }),
      readAll: vi.fn().mockReturnValue({
        fetchAll: vi.fn().mockResolvedValue({
          resources: [
            { id: 'events' },
            { id: 'snapshots' }
          ]
        })
      })
    },
    container: vi.fn().mockReturnValue(mockContainer)
  }

  return {
    CosmosClient: vi.fn(),
    Database: vi.fn(),
    Container: vi.fn(),
    PartitionKeyDefinition: vi.fn()
  }
})

describe('CosmosEventStore with Mocks', () => {
  let mockDatabase: any
  let eventStore: CosmosEventStore

  beforeEach(() => {
    // Create mock database
    mockDatabase = {
      containers: {
        createIfNotExists: vi.fn().mockResolvedValue({
          container: {
            items: {
              create: vi.fn().mockResolvedValue({}),
              query: vi.fn().mockReturnValue({
                fetchAll: vi.fn().mockResolvedValue({ resources: [] })
              }),
              batch: vi.fn().mockImplementation((partitionKey: string) => ({
                create: vi.fn().mockReturnThis(),
                execute: vi.fn().mockResolvedValue({ result: true })
              }))
            }
          }
        })
      }
    }

    eventStore = new CosmosEventStore(mockDatabase as any)
  })

  describe('initialize', () => {
    it('should create containers', async () => {
      const result = await eventStore.initialize()
      
      expect(result.isOk()).toBe(true)
      expect(mockDatabase.containers.createIfNotExists).toHaveBeenCalledTimes(2)
      expect(mockDatabase.containers.createIfNotExists).toHaveBeenCalledWith(
        expect.objectContaining({
          id: 'events',
          partitionKey: expect.objectContaining({
            paths: ['/aggregateId']
          })
        })
      )
    })
  })

  describe('saveEvents', () => {
    it('should save a single event', async () => {
      await eventStore.initialize()

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
    })

    it('should use TransactionalBatch for multiple events', async () => {
      await eventStore.initialize()

      const partitionKeys = PartitionKeys.generate('TestAggregate')
      const events: IEvent[] = [
        {
          sortableUniqueId: SortableUniqueId.generate(new Date()).value,
          eventType: 'Event1',
          payload: { value: 1 },
          aggregateId: partitionKeys.aggregateId,
          partitionKeys,
          version: 1
        },
        {
          sortableUniqueId: SortableUniqueId.generate(new Date()).value,
          eventType: 'Event2',
          payload: { value: 2 },
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
    })
  })

  describe('loadEvents', () => {
    it('should handle empty results', async () => {
      await eventStore.initialize()

      const partitionKeys = PartitionKeys.generate('TestAggregate')
      const result = await eventStore.loadEventsByPartitionKey(partitionKeys)
      
      expect(result.isOk()).toBe(true)
      if (result.isOk()) {
        expect(result.value).toEqual([])
      }
    })
  })
})