import { describe, it, expect } from 'vitest'
import type { Event, EventDocument } from './types'
import { SortableUniqueId } from '../documents/sortable-unique-id'
import { PartitionKeys } from '../documents/partition-keys'

// Test event implementations
class ProductCreated implements Event {
  constructor(
    public readonly productId: string,
    public readonly name: string,
    public readonly price: number
  ) {}
}

class ProductPriceChanged implements Event {
  constructor(
    public readonly productId: string,
    public readonly oldPrice: number,
    public readonly newPrice: number
  ) {}
}

class ProductDiscontinued implements Event {
  constructor(
    public readonly productId: string,
    public readonly reason: string
  ) {}
}

// Empty event for testing
class EmptyEvent implements Event {}

describe('Event Types', () => {
  describe('Event interface', () => {
    it('should be implemented by domain events', () => {
      // Arrange & Act
      const event: Event = new ProductCreated('prod-123', 'Laptop', 999.99)
      
      // Assert
      expect(event).toBeDefined()
      expect(typeof event).toBe('object')
    })
    
    it('should support events with different payloads', () => {
      // Arrange & Act
      const events: Event[] = [
        new ProductCreated('prod-123', 'Laptop', 999.99),
        new ProductPriceChanged('prod-123', 999.99, 899.99),
        new ProductDiscontinued('prod-123', 'End of life'),
        new EmptyEvent()
      ]
      
      // Assert
      expect(events).toHaveLength(4)
      events.forEach(event => {
        expect(event).toBeDefined()
        expect(typeof event).toBe('object')
      })
    })
    
    it('should preserve event data integrity', () => {
      // Arrange & Act
      const event = new ProductCreated('prod-123', 'Laptop', 999.99)
      
      // Assert
      expect((event as ProductCreated).productId).toBe('prod-123')
      expect((event as ProductCreated).name).toBe('Laptop')
      expect((event as ProductCreated).price).toBe(999.99)
    })
  })
  
  describe('EventDocument interface', () => {
    let partitionKeys: PartitionKeys
    let sortableId: SortableUniqueId
    let timestamp: Date
    
    beforeEach(() => {
      partitionKeys = PartitionKeys.create('prod-123', 'products')
      sortableId = SortableUniqueId.generate()
      timestamp = new Date()
    })
    
    it('should represent event with metadata', () => {
      // Arrange
      const event = new ProductCreated('prod-123', 'Laptop', 999.99)
      
      // Act
      const eventDoc: EventDocument = {
        aggregateId: 'prod-123',
        partitionKeys,
        version: 1,
        eventType: 'ProductCreated',
        payload: event,
        sortableUniqueId: sortableId,
        timestamp,
        metadata: {
          userId: 'user-456',
          correlationId: 'corr-789'
        }
      }
      
      // Assert
      expect(eventDoc.aggregateId).toBe('prod-123')
      expect(eventDoc.partitionKeys).toBe(partitionKeys)
      expect(eventDoc.version).toBe(1)
      expect(eventDoc.eventType).toBe('ProductCreated')
      expect(eventDoc.payload).toBe(event)
      expect(eventDoc.sortableUniqueId).toBe(sortableId)
      expect(eventDoc.timestamp).toBe(timestamp)
      expect(eventDoc.metadata.userId).toBe('user-456')
      expect(eventDoc.metadata.correlationId).toBe('corr-789')
    })
    
    it('should support minimal event document', () => {
      // Arrange
      const event = new EmptyEvent()
      
      // Act
      const eventDoc: EventDocument = {
        aggregateId: 'empty-123',
        partitionKeys,
        version: 1,
        eventType: 'EmptyEvent',
        payload: event,
        sortableUniqueId: sortableId,
        timestamp,
        metadata: {}
      }
      
      // Assert
      expect(eventDoc.aggregateId).toBe('empty-123')
      expect(eventDoc.version).toBe(1)
      expect(eventDoc.eventType).toBe('EmptyEvent')
      expect(eventDoc.payload).toBe(event)
      expect(Object.keys(eventDoc.metadata)).toHaveLength(0)
    })
    
    it('should maintain version sequence', () => {
      // Arrange
      const events = [
        new ProductCreated('prod-123', 'Laptop', 999.99),
        new ProductPriceChanged('prod-123', 999.99, 899.99),
        new ProductDiscontinued('prod-123', 'End of life')
      ]
      
      // Act
      const eventDocs: EventDocument[] = events.map((event, index) => ({
        aggregateId: 'prod-123',
        partitionKeys,
        version: index + 1,
        eventType: event.constructor.name,
        payload: event,
        sortableUniqueId: SortableUniqueId.generate(),
        timestamp: new Date(),
        metadata: {}
      }))
      
      // Assert
      expect(eventDocs).toHaveLength(3)
      expect(eventDocs[0].version).toBe(1)
      expect(eventDocs[1].version).toBe(2)
      expect(eventDocs[2].version).toBe(3)
      expect(eventDocs[0].eventType).toBe('ProductCreated')
      expect(eventDocs[1].eventType).toBe('ProductPriceChanged')
      expect(eventDocs[2].eventType).toBe('ProductDiscontinued')
    })
    
    it('should support different metadata types', () => {
      // Arrange
      const event = new ProductCreated('prod-123', 'Laptop', 999.99)
      
      // Act
      const eventDoc: EventDocument = {
        aggregateId: 'prod-123',
        partitionKeys,
        version: 1,
        eventType: 'ProductCreated',
        payload: event,
        sortableUniqueId: sortableId,
        timestamp,
        metadata: {
          userId: 'user-456',
          ipAddress: '192.168.1.1',
          userAgent: 'Mozilla/5.0',
          sessionId: 'sess-789',
          requestId: 'req-abc',
          customData: {
            source: 'web',
            campaign: 'spring-sale'
          }
        }
      }
      
      // Assert
      expect(eventDoc.metadata.userId).toBe('user-456')
      expect(eventDoc.metadata.ipAddress).toBe('192.168.1.1')
      expect(eventDoc.metadata.customData.source).toBe('web')
      expect(eventDoc.metadata.customData.campaign).toBe('spring-sale')
    })
  })
  
  describe('Event type checking', () => {
    it('should distinguish between different event types', () => {
      // Arrange
      const events: Event[] = [
        new ProductCreated('prod-123', 'Laptop', 999.99),
        new ProductPriceChanged('prod-123', 999.99, 899.99),
        new EmptyEvent()
      ]
      
      // Act & Assert
      expect(events[0]).toBeInstanceOf(ProductCreated)
      expect(events[1]).toBeInstanceOf(ProductPriceChanged)
      expect(events[2]).toBeInstanceOf(EmptyEvent)
      
      expect(events[0]).not.toBeInstanceOf(ProductPriceChanged)
      expect(events[1]).not.toBeInstanceOf(ProductCreated)
    })
    
    it('should support type guards for events', () => {
      // Arrange
      const event: Event = new ProductCreated('prod-123', 'Laptop', 999.99)
      
      // Act & Assert
      if (event instanceof ProductCreated) {
        expect(event.productId).toBe('prod-123')
        expect(event.name).toBe('Laptop')
        expect(event.price).toBe(999.99)
      } else {
        fail('Event should be ProductCreated')
      }
    })
  })
  
  describe('Event immutability', () => {
    it('should maintain event data immutability', () => {
      // Arrange
      const event = new ProductCreated('prod-123', 'Laptop', 999.99)
      const originalId = (event as ProductCreated).productId
      const originalName = (event as ProductCreated).name
      const originalPrice = (event as ProductCreated).price
      
      // Act - Attempt to modify (this should not affect the original)
      const modifiedEvent = { ...event, name: 'Modified Laptop' }
      
      // Assert
      expect((event as ProductCreated).productId).toBe(originalId)
      expect((event as ProductCreated).name).toBe(originalName)
      expect((event as ProductCreated).price).toBe(originalPrice)
      expect((modifiedEvent as any).name).toBe('Modified Laptop')
    })
  })
})
