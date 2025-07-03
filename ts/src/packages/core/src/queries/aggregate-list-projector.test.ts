import { describe, it, expect } from 'vitest'
import {
  AggregateListProjector,
  AggregateListPayload,
  createAggregateListProjector
} from './aggregate-list-projector'
import { IProjector } from '../aggregates/projector-interface'
import { IAggregatePayload } from '../aggregates/aggregate-payload'
import { IEventPayload } from '../events/event-payload'
import { IEvent, createEvent } from '../events/event'
import { PartitionKeys } from '../documents/partition-keys'
import { Aggregate } from '../aggregates/aggregate'
import { SortableUniqueId } from '../documents/sortable-unique-id'

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

class UserDeleted implements IEventPayload {}

// Test payloads
import { EmptyAggregatePayload } from '../aggregates/aggregate'

class ActiveUser implements IAggregatePayload {
  constructor(
    public readonly name: string,
    public readonly email: string
  ) {}
}

class DeletedUser implements IAggregatePayload {
  constructor(
    public readonly deletedAt: Date
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
    
    if (payload instanceof ActiveUser && event instanceof UserDeleted) {
      return new DeletedUser(new Date())
    }
    
    return payload
  }
}

describe('AggregateListProjector', () => {
  let userProjector: UserProjector
  let aggregateListProjector: AggregateListProjector<UserProjector>
  
  beforeEach(() => {
    userProjector = new UserProjector()
    aggregateListProjector = createAggregateListProjector(userProjector)
  })
  
  describe('Initialization', () => {
    it('should create aggregate list projector', () => {
      // Assert
      expect(aggregateListProjector).toBeDefined()
      expect(aggregateListProjector.getTypeName()).toBe('AggregateList<UserProjector>')
      expect(aggregateListProjector.getVersion()).toBe(1)
    })
    
    it('should have empty initial state', () => {
      // Act
      const initialState = aggregateListProjector.getInitialState()
      
      // Assert
      expect(initialState).toBeInstanceOf(AggregateListPayload)
      expect(initialState.aggregates.size).toBe(0)
    })
  })
  
  describe('Event projection', () => {
    it('should add new aggregate on first event', () => {
      // Arrange
      const initialState = aggregateListProjector.getInitialState()
      const partitionKeys = PartitionKeys.create('user-123', 'users')
      
      const event = createEvent({
        partitionKeys,
        aggregateType: 'User',
        version: 1,
        payload: new UserCreated('John Doe', 'john@example.com')
      })
      
      // Act
      const newState = aggregateListProjector.project(initialState, event)
      
      // Assert
      expect(newState.aggregates.size).toBe(1)
      
      const aggregate = newState.aggregates.get(partitionKeys.toString())
      expect(aggregate).toBeDefined()
      expect(aggregate?.version).toBe(1)
      expect(aggregate?.payload).toBeInstanceOf(ActiveUser)
      
      const user = aggregate?.payload as ActiveUser
      expect(user.name).toBe('John Doe')
      expect(user.email).toBe('john@example.com')
    })
    
    it('should update existing aggregate', () => {
      // Arrange
      const partitionKeys = PartitionKeys.create('user-123', 'users')
      
      // Create initial state with one user
      const initialAggregate = new Aggregate(
        partitionKeys,
        'User',
        1,
        new ActiveUser('John', 'john@old.com'),
        SortableUniqueId.generate(),
        'UserProjector',
        1
      )
      
      const initialState = new AggregateListPayload(
        new Map([[partitionKeys.toString(), initialAggregate]])
      )
      
      const updateEvent = createEvent({
        partitionKeys,
        aggregateType: 'User',
        version: 2,
        payload: new UserUpdated(undefined, 'john@new.com')
      })
      
      // Act
      const newState = aggregateListProjector.project(initialState, updateEvent)
      
      // Assert
      expect(newState.aggregates.size).toBe(1)
      
      const aggregate = newState.aggregates.get(partitionKeys.toString())
      expect(aggregate?.version).toBe(2)
      expect(aggregate?.lastSortableUniqueId).toBe(updateEvent.id)
      
      const user = aggregate?.payload as ActiveUser
      expect(user.name).toBe('John') // Unchanged
      expect(user.email).toBe('john@new.com') // Updated
    })
    
    it('should handle multiple aggregates', () => {
      // Arrange
      const user1Keys = PartitionKeys.create('user-1', 'users')
      const user2Keys = PartitionKeys.create('user-2', 'users')
      
      const aggregate1 = new Aggregate(
        user1Keys,
        'User',
        1,
        new ActiveUser('Alice', 'alice@test.com'),
        SortableUniqueId.generate(),
        'UserProjector',
        1
      )
      
      const initialState = new AggregateListPayload(
        new Map([[user1Keys.toString(), aggregate1]])
      )
      
      const event = createEvent({
        partitionKeys: user2Keys,
        aggregateType: 'User',
        version: 1,
        payload: new UserCreated('Bob', 'bob@test.com')
      })
      
      // Act
      const newState = aggregateListProjector.project(initialState, event)
      
      // Assert
      expect(newState.aggregates.size).toBe(2)
      expect(newState.aggregates.has(user1Keys.toString())).toBe(true)
      expect(newState.aggregates.has(user2Keys.toString())).toBe(true)
      
      const alice = newState.aggregates.get(user1Keys.toString())?.payload as ActiveUser
      expect(alice.name).toBe('Alice')
      
      const bob = newState.aggregates.get(user2Keys.toString())?.payload as ActiveUser
      expect(bob.name).toBe('Bob')
    })
    
    it('should ignore events from different aggregate types', () => {
      // Arrange
      const initialState = aggregateListProjector.getInitialState()
      
      const event = createEvent({
        partitionKeys: PartitionKeys.create('order-123', 'orders'),
        aggregateType: 'Order', // Different aggregate type
        version: 1,
        payload: new UserCreated('Should be ignored', 'ignored@test.com')
      })
      
      // Act
      const newState = aggregateListProjector.project(initialState, event)
      
      // Assert
      expect(newState).toBe(initialState) // State unchanged
      expect(newState.aggregates.size).toBe(0)
    })
  })
  
  describe('Query support', () => {
    it('should provide access to all aggregates', () => {
      // Arrange
      const aggregates = new Map([
        ['user-1', new Aggregate(
          PartitionKeys.create('user-1', 'users'),
          'User',
          1,
          new ActiveUser('User 1', 'user1@test.com'),
          SortableUniqueId.generate(),
          'UserProjector',
          1
        )],
        ['user-2', new Aggregate(
          PartitionKeys.create('user-2', 'users'),
          'User',
          1,
          new ActiveUser('User 2', 'user2@test.com'),
          SortableUniqueId.generate(),
          'UserProjector',
          1
        )]
      ])
      
      const state = new AggregateListPayload(aggregates)
      
      // Act
      const allAggregates = Array.from(state.aggregates.values())
      
      // Assert
      expect(allAggregates).toHaveLength(2)
      expect(allAggregates[0]?.payload).toBeInstanceOf(ActiveUser)
      expect(allAggregates[1]?.payload).toBeInstanceOf(ActiveUser)
    })
    
    it('should support filtering aggregates', () => {
      // Arrange
      const aggregates = new Map([
        ['user-1', new Aggregate(
          PartitionKeys.create('user-1', 'users'),
          'User',
          1,
          new ActiveUser('Alice', 'alice@test.com'),
          SortableUniqueId.generate(),
          'UserProjector',
          1
        )],
        ['user-2', new Aggregate(
          PartitionKeys.create('user-2', 'users'),
          'User',
          2,
          new DeletedUser(new Date()),
          SortableUniqueId.generate(),
          'UserProjector',
          1
        )],
        ['user-3', new Aggregate(
          PartitionKeys.create('user-3', 'users'),
          'User',
          1,
          new ActiveUser('Bob', 'bob@test.com'),
          SortableUniqueId.generate(),
          'UserProjector',
          1
        )]
      ])
      
      const state = new AggregateListPayload(aggregates)
      
      // Act - filter active users
      const activeUsers = Array.from(state.aggregates.values())
        .filter(agg => agg.payload instanceof ActiveUser)
        .map(agg => agg.payload as ActiveUser)
      
      // Assert
      expect(activeUsers).toHaveLength(2)
      expect(activeUsers[0]?.name).toBe('Alice')
      expect(activeUsers[1]?.name).toBe('Bob')
    })
  })
})