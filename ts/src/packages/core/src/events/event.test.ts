import { describe, it, expect } from 'vitest'
import { 
  IEvent, 
  Event,
  EventMetadata,
  createEvent,
  createEventMetadata
} from './event'
import { IEventPayload } from './event-payload'
import { SortableUniqueId } from '../documents/sortable-unique-id'
import { PartitionKeys } from '../documents/partition-keys'

describe('Event', () => {
  describe('IEvent interface', () => {
    it('should have required properties', () => {
      // Arrange
      class UserCreated implements IEventPayload {
        constructor(public readonly name: string, public readonly email: string) {}
      }
      
      const payload = new UserCreated('John', 'john@example.com')
      const partitionKeys = PartitionKeys.create('user-123', 'users')
      
      // Act
      const event: IEvent<UserCreated> = {
        id: SortableUniqueId.generate(),
        partitionKeys,
        aggregateType: 'User',
        version: 1,
        payload,
        metadata: createEventMetadata()
      }
      
      // Assert
      expect(event.id).toBeDefined()
      expect(event.partitionKeys).toBe(partitionKeys)
      expect(event.aggregateType).toBe('User')
      expect(event.version).toBe(1)
      expect(event.payload).toBe(payload)
      expect(event.metadata).toBeDefined()
    })
  })
  
  describe('EventMetadata', () => {
    it('should create metadata with timestamp', () => {
      // Act
      const metadata = createEventMetadata()
      
      // Assert
      expect(metadata.timestamp).toBeInstanceOf(Date)
      expect(metadata.correlationId).toBeUndefined()
      expect(metadata.causationId).toBeUndefined()
      expect(metadata.userId).toBeUndefined()
    })
    
    it('should create metadata with all properties', () => {
      // Arrange
      const correlationId = 'corr-123'
      const causationId = 'cause-456'
      const userId = 'user-789'
      
      // Act
      const metadata = createEventMetadata({
        correlationId,
        causationId,
        userId
      })
      
      // Assert
      expect(metadata.timestamp).toBeInstanceOf(Date)
      expect(metadata.correlationId).toBe(correlationId)
      expect(metadata.causationId).toBe(causationId)
      expect(metadata.userId).toBe(userId)
    })
    
    it('should support custom metadata', () => {
      // Act
      const metadata = createEventMetadata({
        custom: {
          source: 'web-api',
          ipAddress: '192.168.1.1',
          userAgent: 'Mozilla/5.0'
        }
      })
      
      // Assert
      expect(metadata.custom).toEqual({
        source: 'web-api',
        ipAddress: '192.168.1.1',
        userAgent: 'Mozilla/5.0'
      })
    })
  })
  
  describe('Event class', () => {
    it('should create event with constructor', () => {
      // Arrange
      class OrderPlaced implements IEventPayload {
        constructor(
          public readonly orderId: string,
          public readonly amount: number
        ) {}
      }
      
      const id = SortableUniqueId.generate()
      const partitionKeys = PartitionKeys.create('order-123', 'orders')
      const payload = new OrderPlaced('order-123', 99.99)
      const metadata = createEventMetadata({ userId: 'customer-456' })
      
      // Act
      const event = new Event(
        id,
        partitionKeys,
        'Order',
        1,
        payload,
        metadata
      )
      
      // Assert
      expect(event.id).toBe(id)
      expect(event.partitionKeys).toBe(partitionKeys)
      expect(event.aggregateType).toBe('Order')
      expect(event.version).toBe(1)
      expect(event.payload).toBe(payload)
      expect(event.metadata).toBe(metadata)
    })
    
    it('should be immutable', () => {
      // Arrange
      const event = new Event(
        SortableUniqueId.generate(),
        PartitionKeys.create('test-123'),
        'Test',
        1,
        { type: 'TestEvent' },
        createEventMetadata()
      )
      
      // Act & Assert
      expect(() => {
        (event as any).version = 2
      }).toThrow()
      
      expect(() => {
        (event as any).payload = { type: 'Modified' }
      }).toThrow()
    })
  })
  
  describe('createEvent helper', () => {
    it('should create event with minimal parameters', () => {
      // Arrange
      const partitionKeys = PartitionKeys.create('aggregate-123')
      const payload = { type: 'SimpleEvent' }
      
      // Act
      const event = createEvent({
        partitionKeys,
        aggregateType: 'SimpleAggregate',
        version: 1,
        payload
      })
      
      // Assert
      expect(event.id).toBeDefined()
      expect(event.partitionKeys).toBe(partitionKeys)
      expect(event.aggregateType).toBe('SimpleAggregate')
      expect(event.version).toBe(1)
      expect(event.payload).toBe(payload)
      expect(event.metadata.timestamp).toBeInstanceOf(Date)
    })
    
    it('should create event with full parameters', () => {
      // Arrange
      const id = SortableUniqueId.generate()
      const partitionKeys = PartitionKeys.create('aggregate-456', 'group')
      const payload = { status: 'active' }
      const metadata = createEventMetadata({
        userId: 'admin',
        correlationId: 'req-123'
      })
      
      // Act
      const event = createEvent({
        id,
        partitionKeys,
        aggregateType: 'StatusAggregate',
        version: 5,
        payload,
        metadata
      })
      
      // Assert
      expect(event.id).toBe(id)
      expect(event.metadata).toBe(metadata)
      expect(event.version).toBe(5)
    })
  })
  
  describe('Event patterns', () => {
    it('should support domain event pattern', () => {
      // Arrange
      interface CustomerRegistered extends IEventPayload {
        customerId: string
        email: string
        registeredAt: Date
      }
      
      // Act
      const event = createEvent<CustomerRegistered>({
        partitionKeys: PartitionKeys.create('cust-123', 'customers'),
        aggregateType: 'Customer',
        version: 1,
        payload: {
          customerId: 'cust-123',
          email: 'customer@example.com',
          registeredAt: new Date()
        },
        metadata: createEventMetadata({
          userId: 'system',
          custom: { source: 'registration-form' }
        })
      })
      
      // Assert
      expect(event.payload.customerId).toBe('cust-123')
      expect(event.metadata.custom?.source).toBe('registration-form')
    })
    
    it('should support event versioning pattern', () => {
      // Arrange
      interface ProductPriceChangedV1 extends IEventPayload {
        productId: string
        oldPrice: number
        newPrice: number
      }
      
      interface ProductPriceChangedV2 extends IEventPayload {
        productId: string
        oldPrice: number
        newPrice: number
        currency: string
        reason?: string
      }
      
      // Act - both versions can coexist
      const v1Event = createEvent<ProductPriceChangedV1>({
        partitionKeys: PartitionKeys.create('prod-123', 'products'),
        aggregateType: 'Product',
        version: 10,
        payload: {
          productId: 'prod-123',
          oldPrice: 100,
          newPrice: 120
        }
      })
      
      const v2Event = createEvent<ProductPriceChangedV2>({
        partitionKeys: PartitionKeys.create('prod-123', 'products'),
        aggregateType: 'Product',
        version: 11,
        payload: {
          productId: 'prod-123',
          oldPrice: 120,
          newPrice: 110,
          currency: 'USD',
          reason: 'Seasonal discount'
        }
      })
      
      // Assert
      expect(v1Event.version).toBe(10)
      expect(v2Event.version).toBe(11)
      expect((v2Event.payload as ProductPriceChangedV2).currency).toBe('USD')
    })
  })
})