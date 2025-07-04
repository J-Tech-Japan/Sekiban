import { describe, it, expect, beforeEach, vi } from 'vitest'
import type { EventStore } from './store'
import type { Event, EventDocument } from './types'
import { PartitionKeys } from '../documents/partition-keys'
import { SortableUniqueId } from '../documents/sortable-unique-id'
import { ok, err } from 'neverthrow'
import { NotFoundError } from '../result/errors'

// Test events
class OrderCreated implements Event {
  constructor(
    public readonly orderId: string,
    public readonly customerId: string,
    public readonly amount: number
  ) {}
}

class OrderConfirmed implements Event {
  constructor(public readonly orderId: string) {}
}

class OrderCancelled implements Event {
  constructor(
    public readonly orderId: string,
    public readonly reason: string
  ) {}
}

// Mock implementation of EventStore for testing
class MockEventStore implements EventStore {
  private events: Map<string, EventDocument[]> = new Map()
  private snapshots: Map<string, { payload: any; version: number }> = new Map()
  
  async appendEvents(
    partitionKeys: PartitionKeys,
    events: Event[],
    expectedVersion?: number
  ) {
    const key = partitionKeys.toString()
    const existingEvents = this.events.get(key) || []
    
    if (expectedVersion !== undefined && existingEvents.length !== expectedVersion) {
      return err(new Error('Version mismatch'))
    }
    
    const newEvents: EventDocument[] = events.map((event, index) => ({
      aggregateId: partitionKeys.aggregateId,
      partitionKeys,
      version: existingEvents.length + index + 1,
      eventType: event.constructor.name,
      payload: event,
      sortableUniqueId: SortableUniqueId.generate(),
      timestamp: new Date(),
      metadata: {}
    }))
    
    this.events.set(key, [...existingEvents, ...newEvents])
    return ok(newEvents)
  }
  
  async getEvents(partitionKeys: PartitionKeys, fromVersion?: number) {
    const key = partitionKeys.toString()
    const events = this.events.get(key) || []
    
    if (fromVersion !== undefined) {
      return ok(events.filter(e => e.version >= fromVersion))
    }
    
    return ok(events)
  }
  
  async saveSnapshot(partitionKeys: PartitionKeys, payload: any, version: number) {
    const key = partitionKeys.toString()
    this.snapshots.set(key, { payload, version })
    return ok(undefined)
  }
  
  async getSnapshot(partitionKeys: PartitionKeys) {
    const key = partitionKeys.toString()
    const snapshot = this.snapshots.get(key)
    
    if (!snapshot) {
      return err(new NotFoundError('Snapshot', key))
    }
    
    return ok(snapshot)
  }
  
  async getLastEvent(partitionKeys: PartitionKeys) {
    const events = await this.getEvents(partitionKeys)
    if (events.isErr()) {
      return events
    }
    
    const eventList = events.value
    if (eventList.length === 0) {
      return err(new NotFoundError('Event', partitionKeys.toString()))
    }
    
    return ok(eventList[eventList.length - 1])
  }
  
  // Test helper methods
  clear() {
    this.events.clear()
    this.snapshots.clear()
  }
  
  getEventCount(partitionKeys: PartitionKeys): number {
    const key = partitionKeys.toString()
    return this.events.get(key)?.length || 0
  }
}

describe('EventStore', () => {
  let eventStore: MockEventStore
  let partitionKeys: PartitionKeys
  
  beforeEach(() => {
    eventStore = new MockEventStore()
    partitionKeys = PartitionKeys.create('order-123', 'orders')
  })
  
  describe('appendEvents', () => {
    it('should append single event successfully', async () => {
      // Arrange
      const event = new OrderCreated('order-123', 'customer-456', 99.99)
      
      // Act
      const result = await eventStore.appendEvents(partitionKeys, [event])
      
      // Assert
      expect(result.isOk()).toBe(true)
      const eventDocs = result._unsafeUnwrap()
      expect(eventDocs).toHaveLength(1)
      expect(eventDocs[0].version).toBe(1)
      expect(eventDocs[0].eventType).toBe('OrderCreated')
      expect(eventDocs[0].aggregateId).toBe('order-123')
    })
    
    it('should append multiple events successfully', async () => {
      // Arrange
      const events = [
        new OrderCreated('order-123', 'customer-456', 99.99),
        new OrderConfirmed('order-123')
      ]
      
      // Act
      const result = await eventStore.appendEvents(partitionKeys, events)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const eventDocs = result._unsafeUnwrap()
      expect(eventDocs).toHaveLength(2)
      expect(eventDocs[0].version).toBe(1)
      expect(eventDocs[1].version).toBe(2)
      expect(eventDocs[0].eventType).toBe('OrderCreated')
      expect(eventDocs[1].eventType).toBe('OrderConfirmed')
    })
    
    it('should respect expected version for optimistic concurrency', async () => {
      // Arrange
      const firstEvent = new OrderCreated('order-123', 'customer-456', 99.99)
      await eventStore.appendEvents(partitionKeys, [firstEvent])
      
      const secondEvent = new OrderConfirmed('order-123')
      
      // Act
      const result = await eventStore.appendEvents(partitionKeys, [secondEvent], 1)
      
      // Assert
      expect(result.isOk()).toBe(true)
      expect(eventStore.getEventCount(partitionKeys)).toBe(2)
    })
    
    it('should fail when expected version is incorrect', async () => {
      // Arrange
      const firstEvent = new OrderCreated('order-123', 'customer-456', 99.99)
      await eventStore.appendEvents(partitionKeys, [firstEvent])
      
      const secondEvent = new OrderConfirmed('order-123')
      
      // Act
      const result = await eventStore.appendEvents(partitionKeys, [secondEvent], 0)
      
      // Assert
      expect(result.isErr()).toBe(true)
      expect(eventStore.getEventCount(partitionKeys)).toBe(1)
    })
  })
  
  describe('getEvents', () => {
    beforeEach(async () => {
      const events = [
        new OrderCreated('order-123', 'customer-456', 99.99),
        new OrderConfirmed('order-123'),
        new OrderCancelled('order-123', 'Customer request')
      ]
      await eventStore.appendEvents(partitionKeys, events)
    })
    
    it('should retrieve all events', async () => {
      // Act
      const result = await eventStore.getEvents(partitionKeys)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const events = result._unsafeUnwrap()
      expect(events).toHaveLength(3)
      expect(events[0].eventType).toBe('OrderCreated')
      expect(events[1].eventType).toBe('OrderConfirmed')
      expect(events[2].eventType).toBe('OrderCancelled')
    })
    
    it('should retrieve events from specific version', async () => {
      // Act
      const result = await eventStore.getEvents(partitionKeys, 2)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const events = result._unsafeUnwrap()
      expect(events).toHaveLength(2)
      expect(events[0].version).toBe(2)
      expect(events[1].version).toBe(3)
      expect(events[0].eventType).toBe('OrderConfirmed')
      expect(events[1].eventType).toBe('OrderCancelled')
    })
    
    it('should return empty array for non-existent aggregate', async () => {
      // Arrange
      const newPartitionKeys = PartitionKeys.create('order-999', 'orders')
      
      // Act
      const result = await eventStore.getEvents(newPartitionKeys)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const events = result._unsafeUnwrap()
      expect(events).toHaveLength(0)
    })
    
    it('should return empty array when fromVersion exceeds event count', async () => {
      // Act
      const result = await eventStore.getEvents(partitionKeys, 10)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const events = result._unsafeUnwrap()
      expect(events).toHaveLength(0)
    })
  })
  
  describe('getLastEvent', () => {
    it('should return last event', async () => {
      // Arrange
      const events = [
        new OrderCreated('order-123', 'customer-456', 99.99),
        new OrderConfirmed('order-123')
      ]
      await eventStore.appendEvents(partitionKeys, events)
      
      // Act
      const result = await eventStore.getLastEvent(partitionKeys)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const lastEvent = result._unsafeUnwrap()
      expect(lastEvent.version).toBe(2)
      expect(lastEvent.eventType).toBe('OrderConfirmed')
    })
    
    it('should return error for non-existent aggregate', async () => {
      // Arrange
      const newPartitionKeys = PartitionKeys.create('order-999', 'orders')
      
      // Act
      const result = await eventStore.getLastEvent(newPartitionKeys)
      
      // Assert
      expect(result.isErr()).toBe(true)
      const error = result._unsafeUnwrapErr()
      expect(error.code).toBe('NOT_FOUND')
    })
  })
  
  describe('snapshot operations', () => {
    it('should save and retrieve snapshot', async () => {
      // Arrange
      const snapshotData = {
        orderId: 'order-123',
        customerId: 'customer-456',
        status: 'confirmed',
        amount: 99.99
      }
      
      // Act
      const saveResult = await eventStore.saveSnapshot(partitionKeys, snapshotData, 5)
      expect(saveResult.isOk()).toBe(true)
      
      const getResult = await eventStore.getSnapshot(partitionKeys)
      
      // Assert
      expect(getResult.isOk()).toBe(true)
      const snapshot = getResult._unsafeUnwrap()
      expect(snapshot.version).toBe(5)
      expect(snapshot.payload).toEqual(snapshotData)
    })
    
    it('should return error for non-existent snapshot', async () => {
      // Arrange
      const newPartitionKeys = PartitionKeys.create('order-999', 'orders')
      
      // Act
      const result = await eventStore.getSnapshot(newPartitionKeys)
      
      // Assert
      expect(result.isErr()).toBe(true)
      const error = result._unsafeUnwrapErr()
      expect(error.code).toBe('NOT_FOUND')
    })
  })
  
  describe('concurrency scenarios', () => {
    it('should handle concurrent append operations', async () => {
      // Arrange
      const event1 = new OrderCreated('order-123', 'customer-456', 99.99)
      const event2 = new OrderConfirmed('order-123')
      
      // Act - Simulate concurrent operations
      const [result1, result2] = await Promise.all([
        eventStore.appendEvents(partitionKeys, [event1]),
        eventStore.appendEvents(partitionKeys, [event2])
      ])
      
      // Assert
      expect(result1.isOk()).toBe(true)
      expect(result2.isOk()).toBe(true)
      expect(eventStore.getEventCount(partitionKeys)).toBe(2)
    })
  })
})
