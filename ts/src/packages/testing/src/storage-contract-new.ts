import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import {
  IEventStore,
  PartitionKeys,
  IEvent,
  SortableUniqueId,
  createEvent,
  createEventMetadata,
  EventRetrievalInfo,
  type IEventPayload
} from '@sekiban/core'

// Test event payload
class TestEventPayload implements IEventPayload {
  constructor(public readonly message: string) {}
}

/**
 * Contract test suite for storage providers using the new API
 * This ensures all storage providers behave identically
 */
export async function defineStorageContractTests(
  providerName: string,
  createProvider: () => Promise<IEventStore>,
  cleanup: (provider: IEventStore) => Promise<void>
): Promise<void> {
  describe(`${providerName} Storage Contract Tests`, () => {
    let provider: IEventStore

    beforeEach(async () => {
      provider = await createProvider()
      const initResult = await provider.initialize()
      if (initResult.isErr()) {
        throw new Error(`Failed to initialize provider: ${initResult.error.message}`)
      }
    })

    afterEach(async () => {
      const closeResult = await provider.close()
      if (closeResult.isErr()) {
        console.error(`Failed to close provider: ${closeResult.error.message}`)
      }
      await cleanup(provider)
    })

    describe('Basic Operations', () => {
      it('should save and load a single event', async () => {
        const partitionKeys = PartitionKeys.generate('TestAggregate')
        const event = createEvent({
          partitionKeys,
          aggregateType: 'TestAggregate',
          eventType: 'TestEvent',
          version: 1,
          payload: new TestEventPayload('Hello World'),
          metadata: createEventMetadata({
            userId: 'test-user'
          })
        })

        // Save event
        await provider.saveEvents([event])

        // Load events
        const eventRetrievalInfo = EventRetrievalInfo.fromPartitionKeys(partitionKeys)
        const loadResult = await provider.getEvents(eventRetrievalInfo)
        
        expect(loadResult.isOk()).toBe(true)
        if (loadResult.isOk()) {
          expect(loadResult.value).toHaveLength(1)
          expect(loadResult.value[0].eventType).toBe('TestEvent')
          expect(loadResult.value[0].payload).toEqual({ message: 'Hello World' })
        }
      })

      it('should save and load multiple events', async () => {
        const partitionKeys = PartitionKeys.generate('TestAggregate')
        const events: IEvent[] = []
        
        for (let i = 1; i <= 5; i++) {
          events.push(createEvent({
            partitionKeys,
            aggregateType: 'TestAggregate',
            eventType: `Event${i}`,
            version: i,
            payload: { index: i } as IEventPayload,
            metadata: createEventMetadata()
          }))
        }

        // Save events
        await provider.saveEvents(events)

        // Load events
        const eventRetrievalInfo = EventRetrievalInfo.fromPartitionKeys(partitionKeys)
        const loadResult = await provider.getEvents(eventRetrievalInfo)
        
        expect(loadResult.isOk()).toBe(true)
        if (loadResult.isOk()) {
          expect(loadResult.value).toHaveLength(5)
          for (let i = 0; i < 5; i++) {
            expect(loadResult.value[i].eventType).toBe(`Event${i + 1}`)
            expect(loadResult.value[i].payload).toEqual({ index: i + 1 })
          }
        }
      })

      it('should handle empty partition correctly', async () => {
        const partitionKeys = PartitionKeys.generate('TestAggregate')
        
        // Load events from empty partition
        const eventRetrievalInfo = EventRetrievalInfo.fromPartitionKeys(partitionKeys)
        const loadResult = await provider.getEvents(eventRetrievalInfo)
        
        expect(loadResult.isOk()).toBe(true)
        if (loadResult.isOk()) {
          expect(loadResult.value).toHaveLength(0)
        }
      })

      it('should filter events by version', async () => {
        const partitionKeys = PartitionKeys.generate('TestAggregate')
        const events: IEvent[] = []
        
        // Create 10 events
        for (let i = 1; i <= 10; i++) {
          events.push(createEvent({
            partitionKeys,
            aggregateType: 'TestAggregate',
            eventType: `Event${i}`,
            version: i,
            payload: { value: i } as IEventPayload,
            metadata: createEventMetadata()
          }))
        }

        // Save events
        await provider.saveEvents(events)

        // Load events after version 5
        const eventRetrievalInfo = EventRetrievalInfo.fromPartitionKeys(partitionKeys)
          .withVersionCondition({ afterVersion: 5 })
        const loadResult = await provider.getEvents(eventRetrievalInfo)
        
        expect(loadResult.isOk()).toBe(true)
        if (loadResult.isOk()) {
          expect(loadResult.value).toHaveLength(5)
          expect(loadResult.value[0].version).toBe(6)
          expect(loadResult.value[4].version).toBe(10)
        }
      })

      it('should handle concurrent writes correctly', async () => {
        const partitionKeys = PartitionKeys.generate('TestAggregate')
        
        // Save initial event
        const event1 = createEvent({
          partitionKeys,
          aggregateType: 'TestAggregate',
          eventType: 'Event1',
          version: 1,
          payload: { value: 1 } as IEventPayload,
          metadata: createEventMetadata()
        })
        await provider.saveEvents([event1])

        // Try to save another event with same version (should succeed as saveEvents doesn't check versions)
        const event2 = createEvent({
          partitionKeys,
          aggregateType: 'TestAggregate',
          eventType: 'Event2',
          version: 1,
          payload: { value: 2 } as IEventPayload,
          metadata: createEventMetadata()
        })
        
        // This should succeed as the new API doesn't have version checking in saveEvents
        await provider.saveEvents([event2])
        
        // Both events should be saved
        const eventRetrievalInfo = EventRetrievalInfo.fromPartitionKeys(partitionKeys)
        const loadResult = await provider.getEvents(eventRetrievalInfo)
        
        expect(loadResult.isOk()).toBe(true)
        if (loadResult.isOk()) {
          expect(loadResult.value).toHaveLength(2)
        }
      })

      it('should handle multiple aggregates in same partition', async () => {
        const partitionKey = 'tenant-1'
        const partitionKeys1 = PartitionKeys.generate('TestAggregate', partitionKey)
        const partitionKeys2 = PartitionKeys.generate('TestAggregate', partitionKey)
        
        // Save events for both aggregates
        const event1 = createEvent({
          partitionKeys: partitionKeys1,
          aggregateType: 'TestAggregate',
          eventType: 'Event1',
          version: 1,
          payload: { aggregate: 1 } as IEventPayload,
          metadata: createEventMetadata()
        })
        
        const event2 = createEvent({
          partitionKeys: partitionKeys2,
          aggregateType: 'TestAggregate',
          eventType: 'Event2',
          version: 1,
          payload: { aggregate: 2 } as IEventPayload,
          metadata: createEventMetadata()
        })
        
        await provider.saveEvents([event1, event2])

        // Load events for first aggregate
        const eventRetrievalInfo1 = EventRetrievalInfo.fromPartitionKeys(partitionKeys1)
        const loadResult1 = await provider.getEvents(eventRetrievalInfo1)
        
        expect(loadResult1.isOk()).toBe(true)
        if (loadResult1.isOk()) {
          expect(loadResult1.value).toHaveLength(1)
          expect(loadResult1.value[0].payload).toEqual({ aggregate: 1 })
        }

        // Load events for second aggregate
        const eventRetrievalInfo2 = EventRetrievalInfo.fromPartitionKeys(partitionKeys2)
        const loadResult2 = await provider.getEvents(eventRetrievalInfo2)
        
        expect(loadResult2.isOk()).toBe(true)
        if (loadResult2.isOk()) {
          expect(loadResult2.value).toHaveLength(1)
          expect(loadResult2.value[0].payload).toEqual({ aggregate: 2 })
        }

        // Load all events in partition
        const eventRetrievalInfoAll = EventRetrievalInfo.all()
          .withPartitionKeyFilter(partitionKey)
        const loadResultAll = await provider.getEvents(eventRetrievalInfoAll)
        
        expect(loadResultAll.isOk()).toBe(true)
        if (loadResultAll.isOk()) {
          expect(loadResultAll.value).toHaveLength(2)
        }
      })

      it('should order events by sortable unique id', async () => {
        const partitionKeys = PartitionKeys.generate('TestAggregate')
        const events: IEvent[] = []
        
        // Create events with specific timestamps
        const baseTime = new Date()
        for (let i = 0; i < 5; i++) {
          const timestamp = new Date(baseTime.getTime() + i * 1000)
          events.push(createEvent({
            id: SortableUniqueId.generate(timestamp),
            partitionKeys,
            aggregateType: 'TestAggregate',
            eventType: `Event${i}`,
            version: i + 1,
            payload: { order: i } as IEventPayload,
            metadata: createEventMetadata({ timestamp })
          }))
        }

        // Save events in reverse order
        await provider.saveEvents(events.reverse())

        // Load events
        const eventRetrievalInfo = EventRetrievalInfo.fromPartitionKeys(partitionKeys)
        const loadResult = await provider.getEvents(eventRetrievalInfo)
        
        expect(loadResult.isOk()).toBe(true)
        if (loadResult.isOk()) {
          expect(loadResult.value).toHaveLength(5)
          // Events should be ordered by sortable unique id (timestamp)
          for (let i = 0; i < 5; i++) {
            expect(loadResult.value[i].payload).toEqual({ order: i })
          }
        }
      })
    })
  })
}