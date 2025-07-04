import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import {
  IEventStorageProvider,
  EventBatch,
  PartitionKeys,
  IEvent,
  SortableUniqueId,
  ConcurrencyError,
  SnapshotData
} from '@sekiban/core'

/**
 * Contract test suite for storage providers
 * This ensures all storage providers behave identically
 */
export function defineStorageContractTests(
  providerName: string,
  createProvider: () => Promise<IEventStorageProvider>,
  cleanup: (provider: IEventStorageProvider) => Promise<void>
): void {
  describe(`${providerName} Storage Contract Tests`, () => {
    let provider: IEventStorageProvider

    beforeEach(async () => {
      provider = await createProvider()
    })

    afterEach(async () => {
      await cleanup(provider)
    })

    describe('Basic Operations', () => {
      it('should save and load a single event', async () => {
        const partitionKeys = PartitionKeys.generate('TestAggregate')
        const event: IEvent = {
          sortableUniqueId: SortableUniqueId.generate(new Date()).value,
          eventType: 'TestEvent',
          payload: { message: 'Hello World' },
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
          expect(loadResult.value).toHaveLength(1)
          expect(loadResult.value[0].eventType).toBe('TestEvent')
          expect(loadResult.value[0].payload).toEqual({ message: 'Hello World' })
        }
      })

      it('should save and load multiple events', async () => {
        const partitionKeys = PartitionKeys.generate('TestAggregate')
        const events: IEvent[] = []
        
        for (let i = 1; i <= 5; i++) {
          events.push({
            sortableUniqueId: SortableUniqueId.generate(new Date()).value,
            eventType: `Event${i}`,
            payload: { index: i },
            aggregateId: partitionKeys.aggregateId,
            partitionKeys,
            version: i
          })
        }

        const batch: EventBatch = {
          partitionKeys,
          events,
          expectedVersion: 0
        }

        // Save events
        const saveResult = await provider.saveEvents(batch)
        expect(saveResult.isOk()).toBe(true)

        // Load events
        const loadResult = await provider.loadEventsByPartitionKey(partitionKeys)
        expect(loadResult.isOk()).toBe(true)
        
        if (loadResult.isOk()) {
          expect(loadResult.value).toHaveLength(5)
          for (let i = 0; i < 5; i++) {
            expect(loadResult.value[i].eventType).toBe(`Event${i + 1}`)
            expect(loadResult.value[i].payload).toEqual({ index: i + 1 })
          }
        }
      })

      it('should return empty array for non-existent aggregate', async () => {
        const partitionKeys = PartitionKeys.generate('NonExistent')
        
        const result = await provider.loadEventsByPartitionKey(partitionKeys)
        expect(result.isOk()).toBe(true)
        
        if (result.isOk()) {
          expect(result.value).toEqual([])
        }
      })
    })

    describe('Concurrency Control', () => {
      it('should detect version conflicts', async () => {
        const partitionKeys = PartitionKeys.generate('TestAggregate')
        
        // Save first event
        const event1: IEvent = {
          sortableUniqueId: SortableUniqueId.generate(new Date()).value,
          eventType: 'FirstEvent',
          payload: { value: 1 },
          aggregateId: partitionKeys.aggregateId,
          partitionKeys,
          version: 1
        }

        await provider.saveEvents({
          partitionKeys,
          events: [event1],
          expectedVersion: 0
        })

        // Try to save with wrong expected version
        const event2: IEvent = {
          sortableUniqueId: SortableUniqueId.generate(new Date()).value,
          eventType: 'SecondEvent',
          payload: { value: 2 },
          aggregateId: partitionKeys.aggregateId,
          partitionKeys,
          version: 1
        }

        const result = await provider.saveEvents({
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

    describe('Event Loading', () => {
      it('should load events after specific event ID', async () => {
        const partitionKeys = PartitionKeys.generate('TestAggregate')
        const events: IEvent[] = []
        
        // Save 10 events
        for (let i = 1; i <= 10; i++) {
          events.push({
            sortableUniqueId: SortableUniqueId.generate(new Date()).value,
            eventType: `Event${i}`,
            payload: { index: i },
            aggregateId: partitionKeys.aggregateId,
            partitionKeys,
            version: i
          })
        }

        await provider.saveEvents({
          partitionKeys,
          events,
          expectedVersion: 0
        })

        // Load events after the 5th event
        const afterEventId = events[4].sortableUniqueId
        const result = await provider.loadEvents(partitionKeys, afterEventId)
        
        expect(result.isOk()).toBe(true)
        if (result.isOk()) {
          expect(result.value).toHaveLength(5)
          expect(result.value[0].eventType).toBe('Event6')
          expect(result.value[4].eventType).toBe('Event10')
        }
      })
    })

    describe('Snapshots', () => {
      it('should save and load snapshots', async () => {
        const partitionKeys = PartitionKeys.generate('TestAggregate')
        
        // Initially no snapshot
        const initialResult = await provider.getLatestSnapshot(partitionKeys)
        expect(initialResult.isOk()).toBe(true)
        if (initialResult.isOk()) {
          expect(initialResult.value).toBe(null)
        }

        // Save snapshot
        const snapshot: SnapshotData = {
          partitionKeys,
          version: 10,
          aggregateType: 'TestAggregate',
          payload: { state: 'snapshot at version 10' },
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
          expect(loadResult.value.payload).toEqual({ state: 'snapshot at version 10' })
        }
      })
    })

    describe('Multiple Aggregates', () => {
      it('should isolate events between aggregates', async () => {
        const partitionKeys1 = PartitionKeys.generate('Aggregate1')
        const partitionKeys2 = PartitionKeys.generate('Aggregate2')
        
        // Save events for aggregate 1
        await provider.saveEvents({
          partitionKeys: partitionKeys1,
          events: [{
            sortableUniqueId: SortableUniqueId.generate(new Date()).value,
            eventType: 'Aggregate1Event',
            payload: { agg: 1 },
            aggregateId: partitionKeys1.aggregateId,
            partitionKeys: partitionKeys1,
            version: 1
          }],
          expectedVersion: 0
        })

        // Save events for aggregate 2
        await provider.saveEvents({
          partitionKeys: partitionKeys2,
          events: [{
            sortableUniqueId: SortableUniqueId.generate(new Date()).value,
            eventType: 'Aggregate2Event',
            payload: { agg: 2 },
            aggregateId: partitionKeys2.aggregateId,
            partitionKeys: partitionKeys2,
            version: 1
          }],
          expectedVersion: 0
        })

        // Load events for aggregate 1
        const result1 = await provider.loadEventsByPartitionKey(partitionKeys1)
        expect(result1.isOk()).toBe(true)
        if (result1.isOk()) {
          expect(result1.value).toHaveLength(1)
          expect(result1.value[0].eventType).toBe('Aggregate1Event')
        }

        // Load events for aggregate 2
        const result2 = await provider.loadEventsByPartitionKey(partitionKeys2)
        expect(result2.isOk()).toBe(true)
        if (result2.isOk()) {
          expect(result2.value).toHaveLength(1)
          expect(result2.value[0].eventType).toBe('Aggregate2Event')
        }
      })
    })

    describe('Error Handling', () => {
      it('should handle empty event batch', async () => {
        const partitionKeys = PartitionKeys.generate('TestAggregate')
        
        const batch: EventBatch = {
          partitionKeys,
          events: [],
          expectedVersion: 0
        }

        const result = await provider.saveEvents(batch)
        expect(result.isOk()).toBe(true)
      })

      it('should maintain consistency after errors', async () => {
        const partitionKeys = PartitionKeys.generate('TestAggregate')
        
        // Save first event
        await provider.saveEvents({
          partitionKeys,
          events: [{
            sortableUniqueId: SortableUniqueId.generate(new Date()).value,
            eventType: 'Event1',
            payload: { value: 1 },
            aggregateId: partitionKeys.aggregateId,
            partitionKeys,
            version: 1
          }],
          expectedVersion: 0
        })

        // Try to save with wrong version (should fail)
        const failResult = await provider.saveEvents({
          partitionKeys,
          events: [{
            sortableUniqueId: SortableUniqueId.generate(new Date()).value,
            eventType: 'Event2',
            payload: { value: 2 },
            aggregateId: partitionKeys.aggregateId,
            partitionKeys,
            version: 2
          }],
          expectedVersion: 0
        })

        expect(failResult.isErr()).toBe(true)

        // Verify only first event exists
        const loadResult = await provider.loadEventsByPartitionKey(partitionKeys)
        expect(loadResult.isOk()).toBe(true)
        if (loadResult.isOk()) {
          expect(loadResult.value).toHaveLength(1)
          expect(loadResult.value[0].eventType).toBe('Event1')
        }
      })
    })
  })
}