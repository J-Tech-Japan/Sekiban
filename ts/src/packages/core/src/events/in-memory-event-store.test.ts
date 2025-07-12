import { describe, it, expect, beforeEach } from 'vitest'
import {
  InMemoryEventStore,
  InMemoryEventReader,
  InMemoryEventWriter,
  IEventReader,
  IEventWriter
} from './in-memory-event-store.js'
import { createEvent, IEventPayload } from './event.js'
import { PartitionKeys } from '../documents/partition-keys.js'
import { SortableUniqueId } from '../documents/sortable-unique-id.js'

describe('InMemoryEventStore', () => {
  let store: InMemoryEventStore
  let reader: IEventReader
  let writer: IEventWriter
  
  beforeEach(() => {
    store = new InMemoryEventStore()
    reader = new InMemoryEventReader(store)
    writer = new InMemoryEventWriter(store)
  })
  
  describe('InMemoryEventWriter', () => {
    it('should append single event', async () => {
      // Arrange
      const event = createEvent({
        partitionKeys: PartitionKeys.create('user-123', 'users'),
        aggregateType: 'User',
        version: 1,
        payload: { type: 'UserCreated', name: 'John' }
      })
      
      // Act
      const result = await writer.appendEvent(event)
      
      // Assert
      expect(result.isOk()).toBe(true)
      if (result.isOk()) {
        expect(result.value).toBe(event)
      }
    })
    
    it('should append multiple events', async () => {
      // Arrange
      const events = [
        createEvent({
          partitionKeys: PartitionKeys.create('order-456', 'orders'),
          aggregateType: 'Order',
          version: 1,
          payload: { type: 'OrderCreated' }
        }),
        createEvent({
          partitionKeys: PartitionKeys.create('order-456', 'orders'),
          aggregateType: 'Order',
          version: 2,
          payload: { type: 'OrderConfirmed' }
        })
      ]
      
      // Act
      const result = await writer.appendEvents(events)
      
      // Assert
      expect(result.isOk()).toBe(true)
      if (result.isOk()) {
        expect(result.value).toHaveLength(2)
        expect(result.value[0].version).toBe(1)
        expect(result.value[1].version).toBe(2)
      }
    })
    
    it('should reject duplicate events', async () => {
      // Arrange
      const event = createEvent({
        partitionKeys: PartitionKeys.create('test-123'),
        aggregateType: 'Test',
        version: 1,
        payload: { type: 'TestEvent' }
      })
      
      // Act
      const result1 = await writer.appendEvent(event)
      const result2 = await writer.appendEvent(event)
      
      // Assert
      expect(result1.isOk()).toBe(true)
      expect(result2.isErr()).toBe(true)
      if (result2.isErr()) {
        expect(result2.error.code).toBe('EVENT_STORE_ERROR')
        expect(result2.error.message).toContain('already exists')
      }
    })
    
    it('should enforce version consistency', async () => {
      // Arrange
      const partitionKeys = PartitionKeys.create('entity-789', 'entities')
      
      const event1 = createEvent({
        partitionKeys,
        aggregateType: 'Entity',
        version: 1,
        payload: { type: 'Created' }
      })
      
      const event3 = createEvent({
        partitionKeys,
        aggregateType: 'Entity',
        version: 3, // Skipping version 2
        payload: { type: 'Updated' }
      })
      
      // Act
      await writer.appendEvent(event1)
      const result = await writer.appendEvent(event3)
      
      // Assert
      expect(result.isErr()).toBe(true)
      if (result.isErr()) {
        expect(result.error.code).toBe('CONCURRENCY_ERROR')
        expect(result.error.message).toContain('expected version 2')
      }
    })
  })
  
  describe('InMemoryEventReader', () => {
    beforeEach(async () => {
      // Add some test events
      const events = [
        createEvent({
          partitionKeys: PartitionKeys.create('user-1', 'users'),
          aggregateType: 'User',
          version: 1,
          payload: { type: 'UserCreated', name: 'Alice' }
        }),
        createEvent({
          partitionKeys: PartitionKeys.create('user-1', 'users'),
          aggregateType: 'User',
          version: 2,
          payload: { type: 'UserUpdated', name: 'Alice Smith' }
        }),
        createEvent({
          partitionKeys: PartitionKeys.create('user-2', 'users'),
          aggregateType: 'User',
          version: 1,
          payload: { type: 'UserCreated', name: 'Bob' }
        }),
        createEvent({
          partitionKeys: PartitionKeys.create('order-1', 'orders'),
          aggregateType: 'Order',
          version: 1,
          payload: { type: 'OrderPlaced', total: 100 }
        })
      ]
      
      for (const event of events) {
        await writer.appendEvent(event)
      }
    })
    
    it('should get events by partition keys', async () => {
      // Act
      const result = await reader.getEventsByPartitionKeys(
        PartitionKeys.create('user-1', 'users')
      )
      
      // Assert
      expect(result.isOk()).toBe(true)
      if (result.isOk()) {
        expect(result.value).toHaveLength(2)
        expect(result.value[0].version).toBe(1)
        expect(result.value[1].version).toBe(2)
        expect((result.value[0].payload as any).name).toBe('Alice')
      }
    })
    
    it('should get events from version', async () => {
      // Act
      const result = await reader.getEventsByPartitionKeys(
        PartitionKeys.create('user-1', 'users'),
        2 // From version 2
      )
      
      // Assert
      expect(result.isOk()).toBe(true)
      if (result.isOk()) {
        expect(result.value).toHaveLength(1)
        expect(result.value[0].version).toBe(2)
      }
    })
    
    it('should return empty array for non-existent aggregate', async () => {
      // Act
      const result = await reader.getEventsByPartitionKeys(
        PartitionKeys.create('user-999', 'users')
      )
      
      // Assert
      expect(result.isOk()).toBe(true)
      if (result.isOk()) {
        expect(result.value).toHaveLength(0)
      }
    })
    
    it('should get events by aggregate type', async () => {
      // Act
      const result = await reader.getEventsByAggregateType('User')
      
      // Assert
      expect(result.isOk()).toBe(true)
      if (result.isOk()) {
        expect(result.value).toHaveLength(3)
        expect(result.value.every(e => e.aggregateType === 'User')).toBe(true)
      }
    })
    
    it('should get all events', async () => {
      // Act
      const result = await reader.getAllEvents()
      
      // Assert
      expect(result.isOk()).toBe(true)
      if (result.isOk()) {
        expect(result.value).toHaveLength(4)
      }
    })
    
    it('should get events after specific ID', async () => {
      // Arrange
      const allEvents = await reader.getAllEvents()
      const firstEventId = allEvents.isOk() ? allEvents.value[0].id : null
      
      // Act
      const result = await reader.getEventsAfter(firstEventId!)
      
      // Assert
      expect(result.isOk()).toBe(true)
      if (result.isOk()) {
        expect(result.value).toHaveLength(3)
        expect(result.value.every(e => e.id.toString() > firstEventId!.toString())).toBe(true)
      }
    })
    
    it('should get latest snapshot (when no snapshots exist)', async () => {
      // Act
      const result = await reader.getLatestSnapshot(
        PartitionKeys.create('user-1', 'users')
      )
      
      // Assert
      expect(result.isOk()).toBe(true)
      if (result.isOk()) {
        expect(result.value).toBeNull()
      }
    })
  })
  
  describe('Event ordering', () => {
    it('should maintain chronological order', async () => {
      // Arrange
      const partitionKeys = PartitionKeys.create('timeline-1')
      const events = []
      
      // Create events with small delays to ensure different timestamps
      for (let i = 1; i <= 5; i++) {
        await new Promise(resolve => setTimeout(resolve, 10))
        events.push(createEvent({
          partitionKeys,
          aggregateType: 'Timeline',
          version: i,
          payload: { sequence: i }
        }))
      }
      
      // Act - append in order (version consistency is enforced)
      for (const event of events) {
        await writer.appendEvent(event)
      }
      
      const result = await reader.getEventsByPartitionKeys(partitionKeys)
      
      // Assert - should be returned in version order
      expect(result.isOk()).toBe(true)
      if (result.isOk()) {
        expect(result.value).toHaveLength(5)
        for (let i = 0; i < 5; i++) {
          expect(result.value[i].version).toBe(i + 1)
          expect((result.value[i].payload as any).sequence).toBe(i + 1)
        }
      }
    })
  })
  
  describe('Multi-tenant support', () => {
    it('should isolate events by root partition key', async () => {
      // Arrange
      const tenant1Events = [
        createEvent({
          partitionKeys: PartitionKeys.create('user-1', 'users', 'tenant-1'),
          aggregateType: 'User',
          version: 1,
          payload: { tenant: 'tenant-1' }
        }),
        createEvent({
          partitionKeys: PartitionKeys.create('user-2', 'users', 'tenant-1'),
          aggregateType: 'User',
          version: 1,
          payload: { tenant: 'tenant-1' }
        })
      ]
      
      const tenant2Events = [
        createEvent({
          partitionKeys: PartitionKeys.create('user-1', 'users', 'tenant-2'),
          aggregateType: 'User',
          version: 1,
          payload: { tenant: 'tenant-2' }
        })
      ]
      
      // Act
      for (const event of [...tenant1Events, ...tenant2Events]) {
        await writer.appendEvent(event)
      }
      
      const tenant1Result = await reader.getEventsByPartitionKeys(
        PartitionKeys.create('user-1', 'users', 'tenant-1')
      )
      
      const tenant2Result = await reader.getEventsByPartitionKeys(
        PartitionKeys.create('user-1', 'users', 'tenant-2')
      )
      
      // Assert
      expect(tenant1Result.isOk() && tenant1Result.value).toHaveLength(1)
      expect(tenant2Result.isOk() && tenant2Result.value).toHaveLength(1)
      
      if (tenant1Result.isOk() && tenant2Result.isOk()) {
        expect((tenant1Result.value[0].payload as any).tenant).toBe('tenant-1')
        expect((tenant2Result.value[0].payload as any).tenant).toBe('tenant-2')
      }
    })
  })
})