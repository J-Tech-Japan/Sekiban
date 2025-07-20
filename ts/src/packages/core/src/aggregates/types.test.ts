import { describe, it, expect } from 'vitest'
import type { AggregatePayload, Projector } from './types'
import { Event } from '../events/types'

// Test aggregate payload implementation
interface TestUserPayload extends AggregatePayload {
  userId: string
  name: string
  email: string
  isActive: boolean
}

// Test events
class UserRegistered implements Event {
  constructor(
    public readonly userId: string,
    public readonly name: string,
    public readonly email: string
  ) {}
}

class UserDeactivated implements Event {
  constructor(public readonly userId: string) {}
}

// Test projector implementation
class TestUserProjector implements Projector<TestUserPayload> {
  getInitialState(): TestUserPayload {
    return {
      userId: '',
      name: '',
      email: '',
      isActive: false
    }
  }
  
  apply(state: TestUserPayload, event: Event): TestUserPayload {
    if (event instanceof UserRegistered) {
      return {
        userId: event.userId,
        name: event.name,
        email: event.email,
        isActive: true
      }
    }
    
    if (event instanceof UserDeactivated) {
      return {
        ...state,
        isActive: false
      }
    }
    
    return state
  }
}

describe('Aggregate Types', () => {
  describe('AggregatePayload interface', () => {
    it('should be implemented by domain aggregates', () => {
      // Arrange
      const payload: TestUserPayload = {
        userId: 'user-123',
        name: 'John Doe',
        email: 'john@example.com',
        isActive: true
      }
      
      // Act & Assert
      expect(payload.userId).toBe('user-123')
      expect(payload.name).toBe('John Doe')
      expect(payload.email).toBe('john@example.com')
      expect(payload.isActive).toBe(true)
    })
  })
  
  describe('Projector interface', () => {
    let projector: TestUserProjector
    
    beforeEach(() => {
      projector = new TestUserProjector()
    })
    
    describe('getInitialState', () => {
      it('should return initial aggregate state', () => {
        // Act
        const initialState = projector.getInitialState()
        
        // Assert
        expect(initialState.userId).toBe('')
        expect(initialState.name).toBe('')
        expect(initialState.email).toBe('')
        expect(initialState.isActive).toBe(false)
      })
    })
    
    describe('apply', () => {
      it('should apply UserRegistered event', () => {
        // Arrange
        const initialState = projector.getInitialState()
        const event = new UserRegistered('user-123', 'John Doe', 'john@example.com')
        
        // Act
        const newState = projector.apply(initialState, event)
        
        // Assert
        expect(newState.userId).toBe('user-123')
        expect(newState.name).toBe('John Doe')
        expect(newState.email).toBe('john@example.com')
        expect(newState.isActive).toBe(true)
      })
      
      it('should apply UserDeactivated event', () => {
        // Arrange
        const activeState: TestUserPayload = {
          userId: 'user-123',
          name: 'John Doe',
          email: 'john@example.com',
          isActive: true
        }
        const event = new UserDeactivated('user-123')
        
        // Act
        const newState = projector.apply(activeState, event)
        
        // Assert
        expect(newState.userId).toBe('user-123')
        expect(newState.name).toBe('John Doe')
        expect(newState.email).toBe('john@example.com')
        expect(newState.isActive).toBe(false)
      })
      
      it('should return unchanged state for unknown events', () => {
        // Arrange
        const state = projector.getInitialState()
        const unknownEvent = { type: 'UnknownEvent' } as any
        
        // Act
        const newState = projector.apply(state, unknownEvent)
        
        // Assert
        expect(newState).toEqual(state)
      })
      
      it('should maintain immutability', () => {
        // Arrange
        const originalState = projector.getInitialState()
        const event = new UserRegistered('user-123', 'John Doe', 'john@example.com')
        
        // Act
        const newState = projector.apply(originalState, event)
        
        // Assert
        expect(newState).not.toBe(originalState)
        expect(originalState.userId).toBe('')
        expect(newState.userId).toBe('user-123')
      })
    })
    
    describe('event sequence processing', () => {
      it('should correctly apply sequence of events', () => {
        // Arrange
        const events = [
          new UserRegistered('user-123', 'John Doe', 'john@example.com'),
          new UserDeactivated('user-123')
        ]
        
        // Act
        const finalState = events.reduce(
          (state, event) => projector.apply(state, event),
          projector.getInitialState()
        )
        
        // Assert
        expect(finalState.userId).toBe('user-123')
        expect(finalState.name).toBe('John Doe')
        expect(finalState.email).toBe('john@example.com')
        expect(finalState.isActive).toBe(false)
      })
    })
  })
})
