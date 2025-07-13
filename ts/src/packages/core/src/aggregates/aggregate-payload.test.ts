import { describe, it, expect } from 'vitest'
import { IAggregatePayload, isAggregatePayload } from './aggregate-payload.js'

describe('IAggregatePayload', () => {
  describe('Type marker interface', () => {
    it('should allow any object to implement IAggregatePayload', () => {
      // Arrange
      class User implements IAggregatePayload {
        constructor(
          public readonly id: string,
          public readonly name: string,
          public readonly email: string,
          public readonly isActive: boolean
        ) {}
      }
      
      // Act
      const user = new User('user-123', 'John Doe', 'john@example.com', true)
      
      // Assert
      expect(user).toBeDefined()
      expect(user.id).toBe('user-123')
      expect(user.name).toBe('John Doe')
      expect(user.isActive).toBe(true)
    })
    
    it('should work with object literals', () => {
      // Arrange & Act
      const order: IAggregatePayload = {
        orderId: 'order-456',
        status: 'pending',
        items: [],
        totalAmount: 0
      }
      
      // Assert
      expect(order).toBeDefined()
      expect((order as any).orderId).toBe('order-456')
      expect((order as any).status).toBe('pending')
      expect((order as any).items).toEqual([])
    })
    
    it('should represent empty aggregates', () => {
      // Arrange
      class EmptyAggregate implements IAggregatePayload {}
      
      // Act
      const aggregate = new EmptyAggregate()
      
      // Assert
      expect(aggregate).toBeDefined()
    })
  })
  
  describe('isAggregatePayload type guard', () => {
    it('should return true for objects', () => {
      // Arrange
      const validPayloads = [
        {},
        { id: 'test', state: 'active' },
        { nested: { data: true }, array: [1, 2, 3] },
        new Date(),
        []
      ]
      
      // Act & Assert
      validPayloads.forEach(payload => {
        expect(isAggregatePayload(payload)).toBe(true)
      })
    })
    
    it('should return false for primitives', () => {
      // Arrange
      const invalidPayloads = [
        null,
        undefined,
        123,
        'string value',
        false,
        Symbol('aggregate')
      ]
      
      // Act & Assert
      invalidPayloads.forEach(payload => {
        expect(isAggregatePayload(payload)).toBe(false)
      })
    })
  })
  
  describe('Aggregate state patterns', () => {
    it('should support complex domain aggregates', () => {
      // Arrange
      interface ShoppingCart extends IAggregatePayload {
        cartId: string
        customerId: string
        items: Array<{
          productId: string
          productName: string
          quantity: number
          unitPrice: number
        }>
        appliedCoupons: string[]
        totalAmount: number
        status: 'active' | 'abandoned' | 'checked_out'
      }
      
      // Act
      const cart: ShoppingCart = {
        cartId: 'cart-789',
        customerId: 'customer-123',
        items: [
          {
            productId: 'prod-1',
            productName: 'Widget',
            quantity: 2,
            unitPrice: 9.99
          }
        ],
        appliedCoupons: ['SAVE10'],
        totalAmount: 17.98,
        status: 'active'
      }
      
      // Assert
      expect(cart.items).toHaveLength(1)
      expect(cart.items[0].quantity).toBe(2)
      expect(cart.appliedCoupons).toContain('SAVE10')
      expect(cart.status).toBe('active')
    })
    
    it('should support state machine aggregates', () => {
      // Arrange
      interface WorkflowAggregate extends IAggregatePayload {
        workflowId: string
        currentState: string
        allowedTransitions: string[]
        metadata: Record<string, unknown>
      }
      
      // Act
      const workflow: WorkflowAggregate = {
        workflowId: 'wf-123',
        currentState: 'in_review',
        allowedTransitions: ['approved', 'rejected', 'needs_revision'],
        metadata: {
          reviewer: 'user-456',
          startedAt: new Date().toISOString()
        }
      }
      
      // Assert
      expect(workflow.currentState).toBe('in_review')
      expect(workflow.allowedTransitions).toHaveLength(3)
      expect(workflow.metadata.reviewer).toBe('user-456')
    })
    
    it('should support aggregates with computed properties concept', () => {
      // Note: In TypeScript, we represent computed properties as regular properties
      // that would be calculated during projection
      
      interface Account extends IAggregatePayload {
        accountId: string
        deposits: number[]
        withdrawals: number[]
        balance: number // computed from deposits - withdrawals
      }
      
      // Act
      const account: Account = {
        accountId: 'acc-123',
        deposits: [100, 50, 200],
        withdrawals: [30, 20],
        balance: 300 // 350 - 50
      }
      
      // Assert
      expect(account.balance).toBe(300)
      expect(account.deposits.reduce((a, b) => a + b, 0)).toBe(350)
      expect(account.withdrawals.reduce((a, b) => a + b, 0)).toBe(50)
    })
    
    it('should support nullable/optional fields', () => {
      // Arrange
      interface UserProfile extends IAggregatePayload {
        userId: string
        displayName: string
        bio?: string
        avatarUrl?: string
        lastLoginAt: Date | null
      }
      
      // Act
      const profile: UserProfile = {
        userId: 'user-789',
        displayName: 'JohnD',
        lastLoginAt: null
      }
      
      // Assert
      expect(profile.bio).toBeUndefined()
      expect(profile.avatarUrl).toBeUndefined()
      expect(profile.lastLoginAt).toBeNull()
    })
  })
})