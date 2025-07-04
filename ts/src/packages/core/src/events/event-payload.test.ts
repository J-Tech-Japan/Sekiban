import { describe, it, expect } from 'vitest'
import { IEventPayload, isEventPayload } from './event-payload'

describe('IEventPayload', () => {
  describe('Type marker interface', () => {
    it('should allow any object to implement IEventPayload', () => {
      // Arrange
      class UserCreated implements IEventPayload {
        constructor(
          public readonly userId: string,
          public readonly name: string,
          public readonly email: string
        ) {}
      }
      
      // Act
      const event = new UserCreated('user-123', 'John Doe', 'john@example.com')
      
      // Assert
      expect(event).toBeDefined()
      expect(event.userId).toBe('user-123')
      expect(event.name).toBe('John Doe')
      expect(event.email).toBe('john@example.com')
    })
    
    it('should work with simple object literals', () => {
      // Arrange & Act
      const event: IEventPayload = {
        type: 'OrderPlaced',
        orderId: 'order-456',
        amount: 100.50
      }
      
      // Assert
      expect(event).toBeDefined()
      expect((event as any).type).toBe('OrderPlaced')
      expect((event as any).orderId).toBe('order-456')
      expect((event as any).amount).toBe(100.50)
    })
    
    it('should work with empty events', () => {
      // Arrange
      class StreamStarted implements IEventPayload {}
      
      // Act
      const event = new StreamStarted()
      
      // Assert
      expect(event).toBeDefined()
    })
  })
  
  describe('isEventPayload type guard', () => {
    it('should return true for objects', () => {
      // Arrange
      const validPayloads = [
        {},
        { type: 'test' },
        { nested: { data: true } },
        new Date(),
        []
      ]
      
      // Act & Assert
      validPayloads.forEach(payload => {
        expect(isEventPayload(payload)).toBe(true)
      })
    })
    
    it('should return false for primitives', () => {
      // Arrange
      const invalidPayloads = [
        null,
        undefined,
        42,
        'string',
        true,
        Symbol('test')
      ]
      
      // Act & Assert
      invalidPayloads.forEach(payload => {
        expect(isEventPayload(payload)).toBe(false)
      })
    })
  })
  
  describe('Event payload patterns', () => {
    it('should support state transition events', () => {
      // Arrange
      interface OrderStateChanged extends IEventPayload {
        orderId: string
        fromState: string
        toState: string
        timestamp: Date
      }
      
      // Act
      const event: OrderStateChanged = {
        orderId: 'order-789',
        fromState: 'pending',
        toState: 'confirmed',
        timestamp: new Date()
      }
      
      // Assert
      expect(event.orderId).toBe('order-789')
      expect(event.fromState).toBe('pending')
      expect(event.toState).toBe('confirmed')
    })
    
    it('should support domain events with nested data', () => {
      // Arrange
      interface ProductUpdated extends IEventPayload {
        productId: string
        changes: {
          name?: string
          price?: number
          category?: string
        }
        updatedBy: string
      }
      
      // Act
      const event: ProductUpdated = {
        productId: 'prod-123',
        changes: {
          name: 'New Product Name',
          price: 99.99
        },
        updatedBy: 'admin-user'
      }
      
      // Assert
      expect(event.productId).toBe('prod-123')
      expect(event.changes.name).toBe('New Product Name')
      expect(event.changes.price).toBe(99.99)
      expect(event.changes.category).toBeUndefined()
    })
    
    it('should support events with arrays', () => {
      // Arrange
      interface ItemsAddedToCart extends IEventPayload {
        cartId: string
        items: Array<{
          productId: string
          quantity: number
          price: number
        }>
      }
      
      // Act
      const event: ItemsAddedToCart = {
        cartId: 'cart-456',
        items: [
          { productId: 'prod-1', quantity: 2, price: 10.00 },
          { productId: 'prod-2', quantity: 1, price: 25.00 }
        ]
      }
      
      // Assert
      expect(event.items).toHaveLength(2)
      expect(event.items[0].quantity).toBe(2)
      expect(event.items[1].price).toBe(25.00)
    })
  })
})