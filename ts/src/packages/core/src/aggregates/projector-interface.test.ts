import { describe, it, expect } from 'vitest'
import {
  IAggregateProjector,
  IProjector,
  createProjector,
  ProjectionResult,
  EventOrNone
} from './projector-interface'
import { IAggregatePayload } from './aggregate-payload'
import { IEventPayload } from '../events/event-payload'
import { IEvent, createEvent } from '../events/event'
import { PartitionKeys } from '../documents/partition-keys'

// Test event types
class UserCreated implements IEventPayload {
  constructor(
    public readonly name: string,
    public readonly email: string
  ) {}
}

class UserUpdated implements IEventPayload {
  constructor(
    public readonly name?: string,
    public readonly email?: string
  ) {}
}

class UserDeactivated implements IEventPayload {}

// Test aggregate payload types
class EmptyUser implements IAggregatePayload {}

class ActiveUser implements IAggregatePayload {
  constructor(
    public readonly name: string,
    public readonly email: string,
    public readonly createdAt: Date
  ) {}
}

class InactiveUser implements IAggregatePayload {
  constructor(
    public readonly name: string,
    public readonly email: string,
    public readonly deactivatedAt: Date
  ) {}
}

describe('Projector Interfaces', () => {
  describe('IAggregateProjector', () => {
    it('should define projection method', () => {
      // Arrange
      class UserProjector implements IAggregateProjector<IAggregatePayload> {
        getVersion(): number {
          return 1
        }
        
        project(payload: IAggregatePayload, event: IEventPayload): IAggregatePayload {
          // Pattern matching on payload and event types
          if (payload instanceof EmptyUser && event instanceof UserCreated) {
            return new ActiveUser(event.name, event.email, new Date())
          }
          
          if (payload instanceof ActiveUser && event instanceof UserUpdated) {
            return new ActiveUser(
              event.name ?? payload.name,
              event.email ?? payload.email,
              payload.createdAt
            )
          }
          
          if (payload instanceof ActiveUser && event instanceof UserDeactivated) {
            return new InactiveUser(payload.name, payload.email, new Date())
          }
          
          // Return unchanged for unhandled combinations
          return payload
        }
      }
      
      // Act
      const projector = new UserProjector()
      
      // Test empty → created
      const created = projector.project(new EmptyUser(), new UserCreated('John', 'john@test.com'))
      
      // Assert
      expect(created).toBeInstanceOf(ActiveUser)
      expect((created as ActiveUser).name).toBe('John')
      expect((created as ActiveUser).email).toBe('john@test.com')
      
      // Test update
      const updated = projector.project(created, new UserUpdated(undefined, 'john.doe@test.com'))
      expect(updated).toBeInstanceOf(ActiveUser)
      expect((updated as ActiveUser).email).toBe('john.doe@test.com')
      
      // Test deactivation
      const deactivated = projector.project(updated, new UserDeactivated())
      expect(deactivated).toBeInstanceOf(InactiveUser)
    })
    
    it('should return projector version', () => {
      // Arrange
      class VersionedProjector implements IAggregateProjector<IAggregatePayload> {
        getVersion(): number {
          return 42
        }
        
        project(payload: IAggregatePayload, event: IEventPayload): IAggregatePayload {
          return payload
        }
      }
      
      // Act
      const projector = new VersionedProjector()
      
      // Assert
      expect(projector.getVersion()).toBe(42)
    })
  })
  
  describe('IProjector', () => {
    it('should extend IAggregateProjector with type name', () => {
      // Arrange
      class OrderProjector implements IProjector<IAggregatePayload> {
        getTypeName(): string {
          return 'OrderProjector'
        }
        
        getVersion(): number {
          return 1
        }
        
        project(payload: IAggregatePayload, event: IEventPayload): IAggregatePayload {
          return payload
        }
      }
      
      // Act
      const projector = new OrderProjector()
      
      // Assert
      expect(projector.getTypeName()).toBe('OrderProjector')
      expect(projector.getVersion()).toBe(1)
    })
  })
  
  describe('ProjectionResult', () => {
    it('should handle successful projection', () => {
      // Arrange
      const newPayload = new ActiveUser('Result', 'result@test.com', new Date())
      
      // Act
      const result = ProjectionResult.success(newPayload)
      
      // Assert
      expect(result.isSuccess).toBe(true)
      expect(result.payload).toBe(newPayload)
      expect(result.error).toBeUndefined()
    })
    
    it('should handle projection error', () => {
      // Arrange
      const errorMessage = 'Invalid state transition'
      
      // Act
      const result = ProjectionResult.error<ActiveUser>(errorMessage)
      
      // Assert
      expect(result.isSuccess).toBe(false)
      expect(result.payload).toBeUndefined()
      expect(result.error).toBe(errorMessage)
    })
  })
  
  describe('EventOrNone', () => {
    it('should create event result', () => {
      // Arrange
      const event = createEvent({
        partitionKeys: PartitionKeys.create('user-1'),
        aggregateType: 'User',
        version: 1,
        payload: new UserCreated('Test', 'test@test.com')
      })
      
      // Act
      const result = EventOrNone.event(event)
      
      // Assert
      expect(result.hasEvent).toBe(true)
      expect(result.event).toBe(event)
    })
    
    it('should create none result', () => {
      // Act
      const result = EventOrNone.none()
      
      // Assert
      expect(result.hasEvent).toBe(false)
      expect(result.event).toBeUndefined()
    })
    
    it('should create multiple events result', () => {
      // Arrange
      const events = [
        createEvent({
          partitionKeys: PartitionKeys.create('user-1'),
          aggregateType: 'User',
          version: 1,
          payload: new UserCreated('Test', 'test@test.com')
        }),
        createEvent({
          partitionKeys: PartitionKeys.create('user-1'),
          aggregateType: 'User',
          version: 2,
          payload: new UserUpdated('Updated', undefined)
        })
      ]
      
      // Act
      const result = EventOrNone.events(events)
      
      // Assert
      expect(result.hasEvent).toBe(true)
      expect(result.events).toEqual(events)
      expect(result.events).toHaveLength(2)
    })
  })
  
  describe('createProjector helper', () => {
    it('should create projector from projection function', () => {
      // Arrange
      const projectionFn = (payload: IAggregatePayload, event: IEventPayload): IAggregatePayload => {
        if (payload instanceof EmptyUser && event instanceof UserCreated) {
          return new ActiveUser(event.name, event.email, new Date())
        }
        return payload
      }
      
      // Act
      const projector = createProjector('SimpleProjector', 1, projectionFn)
      
      // Assert
      expect(projector.getTypeName()).toBe('SimpleProjector')
      expect(projector.getVersion()).toBe(1)
      
      const result = projector.project(new EmptyUser(), new UserCreated('Helper', 'helper@test.com'))
      expect(result).toBeInstanceOf(ActiveUser)
      expect((result as ActiveUser).name).toBe('Helper')
    })
  })
})