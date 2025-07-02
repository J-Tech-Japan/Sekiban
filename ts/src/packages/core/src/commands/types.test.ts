import { describe, it, expect } from 'vitest'
import type { Command, CommandHandler, CommandWithHandler, CommandResult } from './types'
import { Event } from '../events/types'
import { AggregatePayload } from '../aggregates/types'
import { ok, err } from 'neverthrow'
import { ValidationError } from '../result/errors'

// Test aggregate payload
interface UserPayload extends AggregatePayload {
  userId: string
  name: string
  email: string
  isActive: boolean
}

// Test events
class UserCreated implements Event {
  constructor(
    public readonly userId: string,
    public readonly name: string,
    public readonly email: string
  ) {}
}

class UserUpdated implements Event {
  constructor(
    public readonly userId: string,
    public readonly name?: string,
    public readonly email?: string
  ) {}
}

// Test projector (minimal implementation for testing)
class UserProjector {
  getInitialState(): UserPayload {
    return {
      userId: '',
      name: '',
      email: '',
      isActive: false
    }
  }
  
  apply(state: UserPayload, event: Event): UserPayload {
    if (event instanceof UserCreated) {
      return {
        userId: event.userId,
        name: event.name,
        email: event.email,
        isActive: true
      }
    }
    
    if (event instanceof UserUpdated) {
      return {
        ...state,
        name: event.name ?? state.name,
        email: event.email ?? state.email
      }
    }
    
    return state
  }
}

// Test command implementations
class CreateUserCommand implements CommandWithHandler<CreateUserCommand, UserProjector> {
  constructor(
    public readonly name: string,
    public readonly email: string
  ) {}
  
  getHandler(): CommandHandler<CreateUserCommand, UserProjector> {
    return {
      validate: async (command) => {
        if (!command.name || command.name.trim() === '') {
          return err(new ValidationError('Name is required', 'name'))
        }
        if (!command.email || !command.email.includes('@')) {
          return err(new ValidationError('Valid email is required', 'email'))
        }
        return ok(undefined)
      },
      handle: async (command, _) => {
        const userId = `user-${Date.now()}`
        return ok([new UserCreated(userId, command.name, command.email)])
      }
    }
  }
}

class UpdateUserCommand implements CommandWithHandler<UpdateUserCommand, UserProjector, UserPayload> {
  constructor(
    public readonly userId: string,
    public readonly name?: string,
    public readonly email?: string
  ) {}
  
  getHandler(): CommandHandler<UpdateUserCommand, UserProjector, UserPayload> {
    return {
      validate: async (command) => {
        if (!command.name && !command.email) {
          return err(new ValidationError('At least one field must be provided', 'fields'))
        }
        return ok(undefined)
      },
      handle: async (command, state) => {
        if (!state.isActive) {
          return err(new ValidationError('Cannot update inactive user', 'state'))
        }
        return ok([new UserUpdated(command.userId, command.name, command.email)])
      }
    }
  }
}

describe('Command Types', () => {
  describe('Command interface', () => {
    it('should be implemented by command classes', () => {
      // Arrange & Act
      const command: Command = new CreateUserCommand('John Doe', 'john@example.com')
      
      // Assert
      expect(command).toBeDefined()
      expect(typeof command).toBe('object')
    })
  })
  
  describe('CommandHandler interface', () => {
    describe('validate method', () => {
      it('should validate command successfully', async () => {
        // Arrange
        const command = new CreateUserCommand('John Doe', 'john@example.com')
        const handler = command.getHandler()
        
        // Act
        const result = await handler.validate(command)
        
        // Assert
        expect(result.isOk()).toBe(true)
      })
      
      it('should return validation error for invalid name', async () => {
        // Arrange
        const command = new CreateUserCommand('', 'john@example.com')
        const handler = command.getHandler()
        
        // Act
        const result = await handler.validate(command)
        
        // Assert
        expect(result.isErr()).toBe(true)
        const error = result._unsafeUnwrapErr()
        expect(error.code).toBe('VALIDATION_ERROR')
        expect((error as ValidationError).field).toBe('name')
      })
      
      it('should return validation error for invalid email', async () => {
        // Arrange
        const command = new CreateUserCommand('John Doe', 'invalid-email')
        const handler = command.getHandler()
        
        // Act
        const result = await handler.validate(command)
        
        // Assert
        expect(result.isErr()).toBe(true)
        const error = result._unsafeUnwrapErr()
        expect(error.code).toBe('VALIDATION_ERROR')
        expect((error as ValidationError).field).toBe('email')
      })
    })
    
    describe('handle method', () => {
      it('should handle CreateUserCommand successfully', async () => {
        // Arrange
        const command = new CreateUserCommand('John Doe', 'john@example.com')
        const handler = command.getHandler()
        
        // Act
        const result = await handler.handle(command, undefined)
        
        // Assert
        expect(result.isOk()).toBe(true)
        const events = result._unsafeUnwrap()
        expect(events).toHaveLength(1)
        expect(events[0]).toBeInstanceOf(UserCreated)
        expect((events[0] as UserCreated).name).toBe('John Doe')
        expect((events[0] as UserCreated).email).toBe('john@example.com')
      })
      
      it('should handle UpdateUserCommand with existing state', async () => {
        // Arrange
        const command = new UpdateUserCommand('user-123', 'Jane Doe')
        const handler = command.getHandler()
        const existingState: UserPayload = {
          userId: 'user-123',
          name: 'John Doe',
          email: 'john@example.com',
          isActive: true
        }
        
        // Act
        const result = await handler.handle(command, existingState)
        
        // Assert
        expect(result.isOk()).toBe(true)
        const events = result._unsafeUnwrap()
        expect(events).toHaveLength(1)
        expect(events[0]).toBeInstanceOf(UserUpdated)
        expect((events[0] as UserUpdated).name).toBe('Jane Doe')
      })
      
      it('should fail to update inactive user', async () => {
        // Arrange
        const command = new UpdateUserCommand('user-123', 'Jane Doe')
        const handler = command.getHandler()
        const inactiveState: UserPayload = {
          userId: 'user-123',
          name: 'John Doe',
          email: 'john@example.com',
          isActive: false
        }
        
        // Act
        const result = await handler.handle(command, inactiveState)
        
        // Assert
        expect(result.isErr()).toBe(true)
        const error = result._unsafeUnwrapErr()
        expect(error.code).toBe('VALIDATION_ERROR')
      })
    })
  })
  
  describe('CommandWithHandler interface', () => {
    it('should provide handler through getHandler method', () => {
      // Arrange
      const command = new CreateUserCommand('John Doe', 'john@example.com')
      
      // Act
      const handler = command.getHandler()
      
      // Assert
      expect(handler).toBeDefined()
      expect(typeof handler.validate).toBe('function')
      expect(typeof handler.handle).toBe('function')
    })
  })
  
  describe('CommandResult type', () => {
    it('should represent successful command execution', async () => {
      // Arrange
      const command = new CreateUserCommand('John Doe', 'john@example.com')
      const handler = command.getHandler()
      
      // Act
      const result: CommandResult = await handler.handle(command, undefined)
      
      // Assert
      expect(result.isOk()).toBe(true)
      if (result.isOk()) {
        const events = result.value
        expect(Array.isArray(events)).toBe(true)
        expect(events.length).toBeGreaterThan(0)
      }
    })
    
    it('should represent failed command execution', async () => {
      // Arrange
      const command = new UpdateUserCommand('user-123')
      const handler = command.getHandler()
      
      // Act
      const result: CommandResult = await handler.validate(command)
      
      // Assert
      expect(result.isErr()).toBe(true)
      if (result.isErr()) {
        expect(result.error.code).toBe('VALIDATION_ERROR')
      }
    })
  })
})
