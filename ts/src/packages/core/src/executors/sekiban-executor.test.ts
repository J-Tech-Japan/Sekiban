import { describe, it, expect } from 'vitest'
import {
  ISekibanExecutor,
  ICommandExecutor,
  IQueryExecutor,
  InMemorySekibanExecutor,
  CommandResponse,
  QueryResponse,
  ExecutorConfig
} from './sekiban-executor'
import { ICommand, ICommandWithHandler } from '../commands/command'
import { IQuery, IMultiProjectionQuery } from '../queries/query'
import { IProjector } from '../aggregates/projector-interface'
import { IAggregatePayload } from '../aggregates/aggregate-payload'
import { IEventPayload } from '../events/event-payload'
import { PartitionKeys } from '../documents/partition-keys'
import { ICommandContext, ICommandContextWithoutState } from '../commands/command'
import { IQueryContext } from '../queries/query'
import { Result, ok, err } from 'neverthrow'
import { ValidationError, CommandValidationError, QueryExecutionError } from '../result/errors'
import { EventOrNone } from '../aggregates/projector-interface'
import { createEvent } from '../events/event'
import { InMemoryEventStore } from '../events/in-memory-event-store'
import { EmptyAggregatePayload } from '../aggregates/aggregate'
import { MultiProjectionState } from '../queries/multi-projection'

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

// Test payloads
class ActiveUser implements IAggregatePayload {
  constructor(
    public readonly name: string,
    public readonly email: string
  ) {}
}

// Test projector
class UserProjector implements IProjector<IAggregatePayload> {
  getTypeName(): string { return 'UserProjector' }
  getVersion(): number { return 1 }
  
  project(payload: IAggregatePayload, event: IEventPayload): IAggregatePayload {
    if (payload instanceof EmptyAggregatePayload && event instanceof UserCreated) {
      return new ActiveUser(event.name, event.email)
    }
    
    if (payload instanceof ActiveUser && event instanceof UserUpdated) {
      return new ActiveUser(
        event.name ?? payload.name,
        event.email ?? payload.email
      )
    }
    
    return payload
  }
}

// Test commands
class CreateUserCommand implements ICommandWithHandler<CreateUserCommand, UserProjector> {
  constructor(
    public readonly name: string,
    public readonly email: string
  ) {}

  validate() {
    if (!this.name || this.name.length === 0) {
      return err([new ValidationError('Name is required', 'name')])
    }
    if (!this.email || this.email.length === 0) {
      return err([new ValidationError('Email is required', 'email')])
    }
    return ok(undefined)
  }

  getPartitionKeys() {
    return PartitionKeys.generate('users')
  }

  handle(command: CreateUserCommand, context: ICommandContextWithoutState) {
    const event = new UserCreated(command.name, command.email)
    const eventData = context.createEvent(event)
    return ok(EventOrNone.event(eventData))
  }
}

class UpdateUserCommand implements ICommandWithHandler<UpdateUserCommand, UserProjector> {
  constructor(
    public readonly userId: string,
    public readonly name?: string,
    public readonly email?: string
  ) {}

  validate() {
    return ok(undefined)
  }

  getPartitionKeys() {
    return PartitionKeys.existing(this.userId, 'users')
  }

  handle(command: UpdateUserCommand, context: ICommandContext<ActiveUser>) {
    const currentUser = context.aggregate.payload
    if (currentUser instanceof EmptyAggregatePayload) {
      return err(new CommandValidationError('UpdateUserCommand', 'User does not exist'))
    }

    const event = new UserUpdated(command.name, command.email)
    return ok(EventOrNone.event(context.createEvent(event)))
  }
}

// Test queries
class GetUserByIdQuery implements IQuery {
  constructor(public readonly userId: string) {}
}

class GetAllUsersQuery implements IMultiProjectionQuery<any, GetAllUsersQuery, ActiveUser[]> {
  constructor() {}
}

describe('Executor Interfaces', () => {
  let eventStore: InMemoryEventStore
  let userProjector: UserProjector
  let executor: InMemorySekibanExecutor

  beforeEach(() => {
    eventStore = new InMemoryEventStore()
    userProjector = new UserProjector()
    executor = new InMemorySekibanExecutor({
      eventStore,
      projectors: [userProjector]
    })
  })

  describe('ISekibanExecutor', () => {
    it('should define command execution interface', () => {
      // Act & Assert
      expect(typeof executor.commandAsync).toBe('function')
    })

    it('should define query execution interface', () => {
      // Act & Assert
      expect(typeof executor.queryAsync).toBe('function')
    })

    it('should define multi-projection query execution interface', () => {
      // Act & Assert
      expect(typeof executor.multiProjectionQueryAsync).toBe('function')
    })
  })

  describe('ICommandExecutor', () => {
    it('should define execute command interface', () => {
      // Create mock executor that implements ICommandExecutor
      const commandExecutor: ICommandExecutor = {
        executeCommandAsync: async () => ({ success: true, aggregateId: 'test', version: 1 } as any)
      }
      
      // Act & Assert
      expect(typeof commandExecutor.executeCommandAsync).toBe('function')
    })
  })

  describe('IQueryExecutor', () => {
    it('should define execute query interface', () => {
      // Create mock executor that implements IQueryExecutor
      const queryExecutor: IQueryExecutor = {
        executeQueryAsync: async () => ({ data: {} } as any),
        executeMultiProjectionQueryAsync: async () => ({ data: [] } as any)
      }
      
      // Act & Assert
      expect(typeof queryExecutor.executeQueryAsync).toBe('function')
    })

    it('should define execute multi-projection query interface', () => {
      // Create mock executor that implements IQueryExecutor
      const queryExecutor: IQueryExecutor = {
        executeQueryAsync: async () => ({ data: {} } as any),
        executeMultiProjectionQueryAsync: async () => ({ data: [] } as any)
      }
      
      // Act & Assert
      expect(typeof queryExecutor.executeMultiProjectionQueryAsync).toBe('function')
    })
  })
})

describe('InMemorySekibanExecutor', () => {
  let executor: InMemorySekibanExecutor
  let eventStore: InMemoryEventStore
  let userProjector: UserProjector

  beforeEach(() => {
    eventStore = new InMemoryEventStore()
    userProjector = new UserProjector()
    executor = new InMemorySekibanExecutor({
      eventStore,
      projectors: [userProjector]
    })
  })

  describe('Command execution', () => {
    it('should execute valid create command', async () => {
      // Arrange
      const command = new CreateUserCommand('John Doe', 'john@example.com')
      
      // Act
      const result = await executor.commandAsync(command)
      
      // Assert
      if (result.isErr()) {
        console.error('Command failed with error:', result.error)
        console.error('Error stack:', result.error.stack)
      }
      expect(result.isOk()).toBe(true)
      const response = result._unsafeUnwrap()
      expect(response.success).toBe(true)
      expect(response.version).toBe(1)
      expect(response.aggregateId).toBeDefined()
    })

    it('should reject invalid command', async () => {
      // Arrange
      const command = new CreateUserCommand('', 'john@example.com')
      
      // Act
      const result = await executor.commandAsync(command)
      
      // Assert
      expect(result.isErr()).toBe(true)
      const error = result._unsafeUnwrapErr()
      expect(error).toBeInstanceOf(ValidationError)
    })

    it('should execute update command on existing aggregate', async () => {
      // Arrange
      const createCommand = new CreateUserCommand('John Doe', 'john@example.com')
      const createResult = await executor.commandAsync(createCommand)
      const aggregateId = createResult._unsafeUnwrap().aggregateId
      
      const updateCommand = new UpdateUserCommand(aggregateId, 'Jane Doe')
      
      // Act
      const result = await executor.commandAsync(updateCommand)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const response = result._unsafeUnwrap()
      expect(response.success).toBe(true)
      expect(response.version).toBe(2)
    })

    it('should handle concurrent command execution', async () => {
      // Arrange
      const command1 = new CreateUserCommand('User 1', 'user1@example.com')
      const command2 = new CreateUserCommand('User 2', 'user2@example.com')
      
      // Act
      const [result1, result2] = await Promise.all([
        executor.commandAsync(command1),
        executor.commandAsync(command2)
      ])
      
      // Assert
      expect(result1.isOk()).toBe(true)
      expect(result2.isOk()).toBe(true)
      
      const response1 = result1._unsafeUnwrap()
      const response2 = result2._unsafeUnwrap()
      
      expect(response1.aggregateId).not.toBe(response2.aggregateId)
    })
  })

  describe('Query execution', () => {
    it('should execute query on existing aggregate', async () => {
      // Arrange
      const createCommand = new CreateUserCommand('John Doe', 'john@example.com')
      const createResult = await executor.commandAsync(createCommand)
      const aggregateId = createResult._unsafeUnwrap().aggregateId
      
      const query = new GetUserByIdQuery(aggregateId)
      
      // Act
      const result = await executor.queryAsync(query, userProjector)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const response = result._unsafeUnwrap()
      expect(response.data).toBeInstanceOf(ActiveUser)
      
      const user = response.data as ActiveUser
      expect(user.name).toBe('John Doe')
      expect(user.email).toBe('john@example.com')
    })

    it('should return error for non-existent aggregate', async () => {
      // Arrange
      const query = new GetUserByIdQuery('non-existent-id')
      
      // Act
      const result = await executor.queryAsync(query, userProjector)
      
      // Assert
      expect(result.isErr()).toBe(true)
      const error = result._unsafeUnwrapErr()
      expect(error).toBeInstanceOf(QueryExecutionError)
    })
  })

  describe('Multi-projection query execution', () => {
    it('should execute multi-projection query', async () => {
      // Arrange
      // Create multiple users
      await executor.commandAsync(new CreateUserCommand('User 1', 'user1@example.com'))
      await executor.commandAsync(new CreateUserCommand('User 2', 'user2@example.com'))
      
      const query = new GetAllUsersQuery()
      
      // Act
      const result = await executor.multiProjectionQueryAsync(query)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const response = result._unsafeUnwrap()
      expect(Array.isArray(response.data)).toBe(true)
      expect(response.data).toHaveLength(2)
    })

    it('should return empty result for no aggregates', async () => {
      // Arrange
      const query = new GetAllUsersQuery()
      
      // Act
      const result = await executor.multiProjectionQueryAsync(query)
      
      // Assert
      expect(result.isOk()).toBe(true)
      const response = result._unsafeUnwrap()
      expect(Array.isArray(response.data)).toBe(true)
      expect(response.data).toHaveLength(0)
    })
  })

  describe('Configuration', () => {
    it('should accept custom configuration', () => {
      // Arrange
      const config: ExecutorConfig = {
        eventStore,
        projectors: [userProjector],
        maxRetries: 5,
        retryDelay: 100
      }
      
      // Act
      const executor = new InMemorySekibanExecutor(config)
      
      // Assert
      expect(executor).toBeDefined()
      expect(executor).toBeInstanceOf(InMemorySekibanExecutor)
    })

    it('should use default configuration values', () => {
      // Arrange
      const config: ExecutorConfig = {
        eventStore,
        projectors: [userProjector]
      }
      
      // Act
      const executor = new InMemorySekibanExecutor(config)
      
      // Assert
      expect(executor).toBeDefined()
    })
  })

  describe('Error handling', () => {
    it('should handle event store errors gracefully', async () => {
      // Arrange
      const faultyEventStore = {
        ...eventStore,
        saveEvents: () => Promise.resolve(err(new Error('Storage failure')))
      } as any
      
      const executor = new InMemorySekibanExecutor({
        eventStore: faultyEventStore,
        projectors: [userProjector]
      })
      
      const command = new CreateUserCommand('John Doe', 'john@example.com')
      
      // Act
      const result = await executor.commandAsync(command)
      
      // Assert
      expect(result.isErr()).toBe(true)
    })

    it('should handle projector errors gracefully', async () => {
      // Arrange
      const faultyProjector = {
        getTypeName: () => 'FaultyProjector',
        getVersion: () => 1,
        project: () => { throw new Error('Projection failure') }
      } as any
      
      const executor = new InMemorySekibanExecutor({
        eventStore,
        projectors: [faultyProjector]
      })
      
      const command = new CreateUserCommand('John Doe', 'john@example.com')
      await executor.commandAsync(command)
      
      const query = new GetUserByIdQuery('some-id')
      
      // Act
      const result = await executor.queryAsync(query, faultyProjector)
      
      // Assert
      expect(result.isErr()).toBe(true)
    })
  })
})