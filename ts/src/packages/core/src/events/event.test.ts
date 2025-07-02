import { describe, it, expect } from 'vitest'
import { SortableUniqueId } from '../documents/sortable-unique-id'
import { PartitionKeys } from '../documents/partition-keys'
import { Event } from './types'
import { EventStream } from './stream'

// Test event payloads
class UserCreated implements Event {
  constructor(
    public readonly userId: string,
    public readonly email: string,
    public readonly name: string
  ) {}
}

class UserUpdated implements Event {
  constructor(
    public readonly userId: string,
    public readonly name?: string,
    public readonly email?: string
  ) {}
}

class UserDeleted implements Event {
  constructor(public readonly userId: string) {}
}

describe('EventStream', () => {
  let stream: EventStream
  let partitionKeys: PartitionKeys
  
  beforeEach(() => {
    partitionKeys = PartitionKeys.create('user-123', 'users')
    stream = new EventStream(partitionKeys)
  })
  
  describe('append', () => {
    it('should append event and increment version', () => {
      // Arrange
      const event = new UserCreated('user-123', 'test@example.com', 'Test User')
      
      // Act
      const document = stream.append(event)
      
      // Assert
      expect(document.version).toBe(1)
      expect(document.aggregateId).toBe('user-123')
      expect(document.eventType).toBe('UserCreated')
      expect(document.payload).toEqual(event)
    })
    
    it('should maintain version sequence', () => {
      // Arrange
      const events = [
        new UserCreated('user-123', 'test@example.com', 'Test User'),
        new UserUpdated('user-123', 'Updated Name'),
        new UserDeleted('user-123')
      ]
      
      // Act
      const documents = events.map(event => stream.append(event))
      
      // Assert
      expect(documents[0].version).toBe(1)
      expect(documents[1].version).toBe(2)
      expect(documents[2].version).toBe(3)
    })
    
    it('should generate unique sortable ids', () => {
      const event1 = new UserCreated('user-123', 'test1@example.com', 'User 1')
      const event2 = new UserCreated('user-456', 'test2@example.com', 'User 2')
      
      const doc1 = stream.append(event1)
      const doc2 = stream.append(event2)
      
      expect(doc1.sortableUniqueId).not.toBe(doc2.sortableUniqueId)
      expect(doc1.sortableUniqueId.value < doc2.sortableUniqueId.value).toBe(true)
    })
  })
  
  describe('getVersion', () => {
    it('should return 0 for empty stream', () => {
      expect(stream.getVersion()).toBe(0)
    })
    
    it('should return current version after appending events', () => {
      stream.append(new UserCreated('user-123', 'test@example.com', 'Test'))
      expect(stream.getVersion()).toBe(1)
      
      stream.append(new UserUpdated('user-123', 'Updated'))
      expect(stream.getVersion()).toBe(2)
    })
  })
  
  describe('getEvents', () => {
    it('should return empty array for empty stream', () => {
      expect(stream.getEvents()).toEqual([])
    })
    
    it('should return all appended events', () => {
      const event1 = new UserCreated('user-123', 'test@example.com', 'Test')
      const event2 = new UserUpdated('user-123', 'Updated')
      
      stream.append(event1)
      stream.append(event2)
      
      const events = stream.getEvents()
      expect(events).toHaveLength(2)
      expect(events[0].payload).toEqual(event1)
      expect(events[1].payload).toEqual(event2)
    })
  })
  
  describe('getEventsFrom', () => {
    it('should return events from specific version', () => {
      // Append 5 events
      for (let i = 1; i <= 5; i++) {
        stream.append(new UserUpdated('user-123', `Update ${i}`))
      }
      
      // Get events from version 3
      const events = stream.getEventsFrom(3)
      
      expect(events).toHaveLength(3)
      expect(events[0].version).toBe(3)
      expect(events[1].version).toBe(4)
      expect(events[2].version).toBe(5)
    })
    
    it('should return empty array when fromVersion exceeds current version', () => {
      stream.append(new UserCreated('user-123', 'test@example.com', 'Test'))
      
      const events = stream.getEventsFrom(10)
      expect(events).toEqual([])
    })
    
    it('should return all events when fromVersion is 0', () => {
      stream.append(new UserCreated('user-123', 'test@example.com', 'Test'))
      stream.append(new UserUpdated('user-123', 'Updated'))
      
      const events = stream.getEventsFrom(0)
      expect(events).toHaveLength(2)
    })
  })
  
  describe('getLastEvent', () => {
    it('should return undefined for empty stream', () => {
      expect(stream.getLastEvent()).toBeUndefined()
    })
    
    it('should return the last appended event', () => {
      const event1 = new UserCreated('user-123', 'test@example.com', 'Test')
      const event2 = new UserUpdated('user-123', 'Updated')
      const event3 = new UserDeleted('user-123')
      
      stream.append(event1)
      stream.append(event2)
      stream.append(event3)
      
      const lastEvent = stream.getLastEvent()
      expect(lastEvent?.payload).toEqual(event3)
      expect(lastEvent?.version).toBe(3)
    })
  })
  
  describe('isEmpty', () => {
    it('should return true for empty stream', () => {
      expect(stream.isEmpty()).toBe(true)
    })
    
    it('should return false after appending events', () => {
      stream.append(new UserCreated('user-123', 'test@example.com', 'Test'))
      expect(stream.isEmpty()).toBe(false)
    })
  })
  
  describe('concurrent operations', () => {
    it('should handle rapid event appending', () => {
      const events = Array.from({ length: 100 }, (_, i) => 
        new UserUpdated('user-123', `Update ${i}`)
      )
      
      events.forEach(event => stream.append(event))
      
      expect(stream.getVersion()).toBe(100)
      expect(stream.getEvents()).toHaveLength(100)
      
      // Verify all sortable IDs are unique
      const ids = stream.getEvents().map(doc => doc.sortableUniqueId.value)
      const uniqueIds = new Set(ids)
      expect(uniqueIds.size).toBe(100)
    })
  })
})
