import { describe, it, expect } from 'vitest'
import {
  ICommand,
  ICommandHandler,
  ICommandWithHandler,
  ICommandContext,
  ICommandContextWithoutState,
  CommandResponse,
  createCommandResponse
} from './command'
import { PartitionKeys } from '../documents/partition-keys'
import { IAggregatePayload } from '../aggregates/aggregate-payload'
import { IProjector } from '../aggregates/projector-interface'
import { IEventPayload } from '../events/event-payload'
import { EventOrNone } from '../aggregates/projector-interface'
import { Result, ok, err } from 'neverthrow'
import { ValidationError, CommandValidationError } from '../result/errors'
import { Aggregate } from '../aggregates/aggregate'
import { SortableUniqueId } from '../documents/sortable-unique-id'

// Test payloads
class UserPayload implements IAggregatePayload {
  constructor(
    public readonly name: string,
    public readonly email: string,
    public readonly isActive: boolean = true
  ) {}
}

// Test events
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

// Test projector
class UserProjector implements IProjector<IAggregatePayload> {
  getTypeName(): string { return 'UserProjector' }
  getVersion(): number { return 1 }
  
  project(payload: IAggregatePayload, event: IEventPayload): IAggregatePayload {
    if (event instanceof UserCreated) {
      return new UserPayload(event.name, event.email)
    }
    if (payload instanceof UserPayload && event instanceof UserUpdated) {
      return new UserPayload(
        event.name ?? payload.name,
        event.email ?? payload.email,
        payload.isActive
      )
    }
    return payload
  }
}

describe('Command Interfaces', () => {
  describe('ICommand', () => {
    it('should be a marker interface', () => {
      // Arrange
      class CreateUser implements ICommand {
        constructor(
          public readonly name: string,
          public readonly email: string
        ) {}
      }
      
      // Act
      const command = new CreateUser('John', 'john@example.com')
      
      // Assert
      expect(command).toBeDefined()
      expect(command.name).toBe('John')
      expect(command.email).toBe('john@example.com')
    })
  })
  
  describe('ICommandHandler', () => {
    it('should define handler interface', () => {
      // Arrange
      class UpdateUser implements ICommand {
        constructor(
          public readonly userId: string,
          public readonly name?: string,
          public readonly email?: string
        ) {}
      }
      
      class UpdateUserHandler implements ICommandHandler<UpdateUser, UserPayload> {
        handle(
          command: UpdateUser,
          context: ICommandContext<UserPayload>
        ): Result<EventOrNone, Error> {
          // Business logic validation
          if (!command.name && !command.email) {
            return err(new ValidationError('At least one field must be provided'))
          }
          
          // Create event
          const event = new UserUpdated(command.name, command.email)
          return ok(EventOrNone.event(context.createEvent(event)))
        }
      }
      
      // Act
      const handler = new UpdateUserHandler()
      const aggregate = new Aggregate(
        PartitionKeys.create('user-123'),
        'User',
        1,
        new UserPayload('Current', 'current@test.com'),
        SortableUniqueId.generate(),
        'UserProjector',
        1
      )
      
      // Create mock context
      const context: ICommandContext<UserPayload> = {
        getAggregate: () => aggregate,
        createEvent: (payload: IEventPayload) => ({
          id: SortableUniqueId.generate(),
          partitionKeys: aggregate.partitionKeys,
          aggregateType: aggregate.aggregateType,
          version: aggregate.version + 1,
          payload,
          metadata: {
            correlationId: 'test-correlation',
            causationId: 'test-causation',
            timestamp: new Date(),
            userId: 'test-user'
          }
        })
      }
      
      const result = handler.handle(
        new UpdateUser('user-123', 'Updated Name'),
        context
      )
      
      // Assert
      expect(result.isOk()).toBe(true)
      if (result.isOk()) {
        expect(result.value.hasEvent).toBe(true)
        expect(result.value.event?.payload).toBeInstanceOf(UserUpdated)
      }
    })
  })
  
  describe('ICommandWithHandler', () => {
    it('should combine command and handler', () => {
      // Arrange
      class CreateUser implements ICommandWithHandler<CreateUser, UserProjector> {
        constructor(
          public readonly name: string,
          public readonly email: string
        ) {}
        
        validate(): Result<void, ValidationError[]> {
          const errors: ValidationError[] = []
          
          if (!this.name || this.name.length < 2) {
            errors.push(new ValidationError('Name must be at least 2 characters'))
          }
          
          if (!this.email || !this.email.includes('@')) {
            errors.push(new ValidationError('Invalid email format'))
          }
          
          return errors.length > 0 ? err(errors) : ok(undefined)
        }
        
        getPartitionKeys(): PartitionKeys {
          // Generate new partition keys for new aggregate
          return PartitionKeys.generate('users')
        }
        
        handle(
          command: CreateUser,
          context: ICommandContextWithoutState
        ): Result<EventOrNone, Error> {
          const event = new UserCreated(command.name, command.email)
          return ok(EventOrNone.event(context.createEvent(event)))
        }
      }
      
      // Act
      const command = new CreateUser('Alice', 'alice@example.com')
      const validation = command.validate()
      
      // Assert
      expect(validation.isOk()).toBe(true)
      expect(command.getPartitionKeys().group).toBe('users')
    })
    
    it('should validate command properties', () => {
      // Arrange
      class InvalidCommand implements ICommandWithHandler<InvalidCommand, UserProjector> {
        constructor(
          public readonly name: string,
          public readonly email: string
        ) {}
        
        validate(): Result<void, ValidationError[]> {
          const errors: ValidationError[] = []
          
          if (!this.name) {
            errors.push(new ValidationError('Name is required'))
          }
          
          if (!this.email.includes('@')) {
            errors.push(new ValidationError('Invalid email'))
          }
          
          return errors.length > 0 ? err(errors) : ok(undefined)
        }
        
        getPartitionKeys(): PartitionKeys {
          return PartitionKeys.generate('users')
        }
        
        handle(
          command: InvalidCommand,
          context: ICommandContextWithoutState
        ): Result<EventOrNone, Error> {
          return ok(EventOrNone.none())
        }
      }
      
      // Act
      const command = new InvalidCommand('', 'invalid-email')
      const validation = command.validate()
      
      // Assert
      expect(validation.isErr()).toBe(true)
      if (validation.isErr()) {
        expect(validation.error).toHaveLength(2)
        expect(validation.error[0]!.message).toBe('Name is required')
        expect(validation.error[1]!.message).toBe('Invalid email')
      }
    })
  })
  
  describe('CommandResponse', () => {
    it('should create success response', () => {
      // Arrange & Act
      const response = createCommandResponse({
        success: true,
        aggregateId: 'user-123',
        version: 2,
        eventId: 'event-456'
      })
      
      // Assert
      expect(response.success).toBe(true)
      expect(response.aggregateId).toBe('user-123')
      expect(response.version).toBe(2)
      expect(response.eventId).toBe('event-456')
      expect(response.error).toBeUndefined()
    })
    
    it('should create error response', () => {
      // Arrange & Act
      const response = createCommandResponse({
        success: false,
        error: 'Validation failed'
      })
      
      // Assert
      expect(response.success).toBe(false)
      expect(response.error).toBe('Validation failed')
      expect(response.aggregateId).toBeUndefined()
    })
  })
  
  describe('Command Context', () => {
    it('should provide access to aggregate state', () => {
      // Arrange
      const aggregate = new Aggregate(
        PartitionKeys.create('test-123'),
        'Test',
        3,
        new UserPayload('Test User', 'test@test.com'),
        SortableUniqueId.generate(),
        'TestProjector',
        1
      )
      
      const context: ICommandContext<UserPayload> = {
        getAggregate: () => aggregate,
        createEvent: (payload: IEventPayload) => ({
          id: SortableUniqueId.generate(),
          partitionKeys: aggregate.partitionKeys,
          aggregateType: aggregate.aggregateType,
          version: aggregate.version + 1,
          payload,
          metadata: {
            correlationId: 'ctx-correlation',
            causationId: 'ctx-causation',
            timestamp: new Date(),
            userId: 'ctx-user'
          }
        })
      }
      
      // Act
      const retrievedAggregate = context.getAggregate()
      
      // Assert
      expect(retrievedAggregate).toBe(aggregate)
      expect(retrievedAggregate.version).toBe(3)
      expect(retrievedAggregate.payload).toBeInstanceOf(UserPayload)
    })
  })
})