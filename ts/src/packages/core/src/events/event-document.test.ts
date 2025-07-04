import { describe, it, expect } from 'vitest'
import {
  EventDocument,
  SerializableEventDocument,
  toSerializableEventDocument,
  fromSerializableEventDocument
} from './event-document'
import { createEvent, IEventPayload } from './event'
import { SortableUniqueId } from '../documents/sortable-unique-id'
import { PartitionKeys } from '../documents/partition-keys'

describe('EventDocument', () => {
  describe('EventDocument class', () => {
    it('should create event document from event', () => {
      // Arrange
      class UserCreated implements IEventPayload {
        constructor(public readonly name: string, public readonly email: string) {}
      }
      
      const event = createEvent({
        partitionKeys: PartitionKeys.create('user-123', 'users'),
        aggregateType: 'User',
        version: 1,
        payload: new UserCreated('John', 'john@example.com')
      })
      
      // Act
      const document = new EventDocument(event)
      
      // Assert
      expect(document.event).toBe(event)
      expect(document.id).toBe(event.id)
      expect(document.partitionKeys).toBe(event.partitionKeys)
      expect(document.aggregateType).toBe(event.aggregateType)
      expect(document.version).toBe(event.version)
      expect(document.payload).toBe(event.payload)
      expect(document.metadata).toBe(event.metadata)
    })
    
    it('should provide convenience accessors', () => {
      // Arrange
      const event = createEvent({
        partitionKeys: PartitionKeys.create('order-456', 'orders'),
        aggregateType: 'Order',
        version: 5,
        payload: { status: 'pending', total: 100.50 }
      })
      
      // Act
      const document = new EventDocument(event)
      
      // Assert
      expect(document.aggregateId).toBe('order-456')
      expect(document.timestamp).toBe(event.metadata.timestamp)
      expect(document.sortableId).toBe(event.id.toString())
    })
  })
  
  describe('SerializableEventDocument', () => {
    it('should convert to serializable format', () => {
      // Arrange
      const event = createEvent({
        partitionKeys: PartitionKeys.create('prod-789', 'products'),
        aggregateType: 'Product',
        version: 3,
        payload: {
          name: 'Widget',
          price: 29.99,
          inStock: true
        },
        metadata: {
          timestamp: new Date('2024-01-01T00:00:00Z'),
          userId: 'admin-user',
          correlationId: 'req-123'
        }
      })
      
      // Act
      const serializable = toSerializableEventDocument(new EventDocument(event))
      
      // Assert
      expect(serializable.id).toBe(event.id.toString())
      expect(serializable.aggregateId).toBe('prod-789')
      expect(serializable.aggregateType).toBe('Product')
      expect(serializable.version).toBe(3)
      expect(serializable.payload).toBe(JSON.stringify(event.payload))
      expect(serializable.payloadTypeName).toBe('Object') // Default for plain objects
      expect(serializable.timestamp).toBe('2024-01-01T00:00:00.000Z')
      expect(serializable.partitionKey).toBe(event.partitionKeys.toString())
      
      // Metadata should be serialized
      const metadata = JSON.parse(serializable.metadata)
      expect(metadata.userId).toBe('admin-user')
      expect(metadata.correlationId).toBe('req-123')
    })
    
    it('should include payload type name when available', () => {
      // Arrange
      class OrderPlaced implements IEventPayload {
        constructor(public readonly orderId: string, public readonly amount: number) {}
      }
      
      const event = createEvent({
        partitionKeys: PartitionKeys.create('order-123'),
        aggregateType: 'Order',
        version: 1,
        payload: new OrderPlaced('order-123', 99.99)
      })
      
      // Act
      const serializable = toSerializableEventDocument(new EventDocument(event))
      
      // Assert
      expect(serializable.payloadTypeName).toBe('OrderPlaced')
    })
    
    it('should handle arrays and complex objects', () => {
      // Arrange
      const event = createEvent({
        partitionKeys: PartitionKeys.create('cart-456'),
        aggregateType: 'ShoppingCart',
        version: 2,
        payload: {
          items: [
            { id: 'item-1', quantity: 2, price: 10.00 },
            { id: 'item-2', quantity: 1, price: 25.00 }
          ],
          discount: { code: 'SAVE10', amount: 4.50 }
        }
      })
      
      // Act
      const serializable = toSerializableEventDocument(new EventDocument(event))
      const parsedPayload = JSON.parse(serializable.payload)
      
      // Assert
      expect(parsedPayload.items).toHaveLength(2)
      expect(parsedPayload.items[0].id).toBe('item-1')
      expect(parsedPayload.discount.code).toBe('SAVE10')
    })
  })
  
  describe('fromSerializableEventDocument', () => {
    it('should reconstruct event document from serializable format', () => {
      // Arrange
      const serializable: SerializableEventDocument = {
        id: '01234567890abcdef01234567890abcd',
        aggregateId: 'user-999',
        aggregateType: 'User',
        version: 7,
        payload: JSON.stringify({ name: 'Jane', role: 'admin' }),
        payloadTypeName: 'UserUpdated',
        timestamp: '2024-02-15T10:30:00.000Z',
        partitionKey: 'users-user-999',
        group: 'users',
        rootPartitionKey: undefined,
        metadata: JSON.stringify({
          userId: 'system',
          correlationId: 'batch-789'
        })
      }
      
      // Act
      const result = fromSerializableEventDocument(serializable)
      
      // Assert
      expect(result.isOk()).toBe(true)
      if (result.isOk()) {
        const document = result.value
        expect(document.id.toString()).toBe(serializable.id)
        expect(document.aggregateId).toBe('user-999')
        expect(document.aggregateType).toBe('User')
        expect(document.version).toBe(7)
        expect(document.payload).toEqual({ name: 'Jane', role: 'admin' })
        expect(document.metadata.userId).toBe('system')
        expect(document.metadata.correlationId).toBe('batch-789')
      }
    })
    
    it('should handle invalid JSON in payload', () => {
      // Arrange
      const serializable: SerializableEventDocument = {
        id: '01234567890abcdef01234567890abcd',
        aggregateId: 'test-123',
        aggregateType: 'Test',
        version: 1,
        payload: 'invalid json {',
        payloadTypeName: 'TestEvent',
        timestamp: new Date().toISOString(),
        partitionKey: 'test-123',
        metadata: '{}'
      }
      
      // Act
      const result = fromSerializableEventDocument(serializable)
      
      // Assert
      expect(result.isErr()).toBe(true)
      if (result.isErr()) {
        expect(result.error.code).toBe('SERIALIZATION_ERROR')
        expect(result.error.message).toContain('Failed to parse event payload')
      }
    })
    
    it('should handle invalid sortable unique id', () => {
      // Arrange
      const serializable: SerializableEventDocument = {
        id: 'invalid-id',
        aggregateId: 'test-123',
        aggregateType: 'Test',
        version: 1,
        payload: '{}',
        payloadTypeName: 'TestEvent',
        timestamp: new Date().toISOString(),
        partitionKey: 'test-123',
        metadata: '{}'
      }
      
      // Act
      const result = fromSerializableEventDocument(serializable)
      
      // Assert
      expect(result.isErr()).toBe(true)
      if (result.isErr()) {
        expect(result.error.code).toBe('VALIDATION_ERROR')
      }
    })
  })
  
  describe('Round-trip serialization', () => {
    it('should maintain data integrity through serialization', () => {
      // Arrange
      const originalEvent = createEvent({
        partitionKeys: PartitionKeys.create('entity-123', 'entities', 'tenant-456'),
        aggregateType: 'Entity',
        version: 42,
        payload: {
          field1: 'value1',
          field2: 123,
          field3: true,
          field4: null,
          field5: ['a', 'b', 'c'],
          field6: { nested: { deep: 'value' } }
        },
        metadata: {
          timestamp: new Date('2024-03-01T15:45:30.123Z'),
          userId: 'user-789',
          correlationId: 'corr-abc',
          causationId: 'cause-xyz',
          custom: {
            source: 'api',
            version: '2.0'
          }
        }
      })
      
      // Act
      const document = new EventDocument(originalEvent)
      const serializable = toSerializableEventDocument(document)
      const result = fromSerializableEventDocument(serializable)
      
      // Assert
      expect(result.isOk()).toBe(true)
      if (result.isOk()) {
        const reconstructed = result.value
        expect(reconstructed.aggregateId).toBe(originalEvent.partitionKeys.aggregateId)
        expect(reconstructed.aggregateType).toBe(originalEvent.aggregateType)
        expect(reconstructed.version).toBe(originalEvent.version)
        expect(reconstructed.payload).toEqual(originalEvent.payload)
        expect(reconstructed.metadata.userId).toBe(originalEvent.metadata.userId)
        expect(reconstructed.metadata.correlationId).toBe(originalEvent.metadata.correlationId)
        expect(reconstructed.metadata.causationId).toBe(originalEvent.metadata.causationId)
        expect(reconstructed.metadata.custom).toEqual(originalEvent.metadata.custom)
      }
    })
  })
})