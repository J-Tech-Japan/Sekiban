import { describe, it, expect } from 'vitest'
import { AggregateProjector } from './aggregate-projector'
import { IProjector } from './projector-interface'
import { IAggregatePayload } from './aggregate-payload'
import { IEventPayload } from '../events/event-payload'
import { IEvent, createEvent } from '../events/event'
import { Aggregate, createEmptyAggregate } from './aggregate'
import { PartitionKeys } from '../documents/partition-keys'
import { SortableUniqueId } from '../documents/sortable-unique-id'

// Test events
class AccountOpened implements IEventPayload {
  constructor(
    public readonly owner: string,
    public readonly initialBalance: number
  ) {}
}

class MoneyDeposited implements IEventPayload {
  constructor(public readonly amount: number) {}
}

class MoneyWithdrawn implements IEventPayload {
  constructor(public readonly amount: number) {}
}

// Test payloads
import { EmptyAggregatePayload } from './aggregate'

class Account implements IAggregatePayload {
  constructor(
    public readonly owner: string,
    public readonly balance: number,
    public readonly isOpen: boolean = true
  ) {}
}

// Test projector
class AccountProjector implements IProjector<IAggregatePayload> {
  getTypeName(): string {
    return 'AccountProjector'
  }
  
  getVersion(): number {
    return 1
  }
  
  project(payload: IAggregatePayload, event: IEventPayload): IAggregatePayload {
    if (payload instanceof EmptyAggregatePayload && event instanceof AccountOpened) {
      return new Account(event.owner, event.initialBalance)
    }
    
    if (payload instanceof Account && event instanceof MoneyDeposited) {
      return new Account(payload.owner, payload.balance + event.amount)
    }
    
    if (payload instanceof Account && event instanceof MoneyWithdrawn) {
      return new Account(payload.owner, payload.balance - event.amount)
    }
    
    return payload
  }
}

describe('AggregateProjector', () => {
  let projector: AggregateProjector<IAggregatePayload>
  let accountProjector: AccountProjector
  let partitionKeys: PartitionKeys
  
  beforeEach(() => {
    accountProjector = new AccountProjector()
    projector = new AggregateProjector(accountProjector)
    partitionKeys = PartitionKeys.create('account-123', 'accounts')
  })
  
  describe('projectEvent', () => {
    it('should project single event onto empty aggregate', () => {
      // Arrange
      const aggregate = createEmptyAggregate(
        partitionKeys,
        'Account',
        'AccountProjector',
        1
      )
      
      const event = createEvent({
        partitionKeys,
        aggregateType: 'Account',
        version: 1,
        payload: new AccountOpened('John Doe', 1000)
      })
      
      // Act
      const result = projector.projectEvent(aggregate, event)
      
      // Assert
      expect(result.version).toBe(1)
      expect(result.lastSortableUniqueId).toBe(event.id)
      expect(result.payload).toBeInstanceOf(Account)
      
      const account = result.payload as Account
      expect(account.owner).toBe('John Doe')
      expect(account.balance).toBe(1000)
      expect(account.isOpen).toBe(true)
    })
    
    it('should project event onto existing aggregate', () => {
      // Arrange
      const existingAggregate = new Aggregate(
        partitionKeys,
        'Account',
        1,
        new Account('Jane Smith', 500),
        SortableUniqueId.generate(),
        'AccountProjector',
        1
      )
      
      const depositEvent = createEvent({
        partitionKeys,
        aggregateType: 'Account',
        version: 2,
        payload: new MoneyDeposited(250)
      })
      
      // Act
      const result = projector.projectEvent(existingAggregate, depositEvent)
      
      // Assert
      expect(result.version).toBe(2)
      expect(result.lastSortableUniqueId).toBe(depositEvent.id)
      
      const account = result.payload as Account
      expect(account.owner).toBe('Jane Smith')
      expect(account.balance).toBe(750)
    })
  })
  
  describe('projectEvents', () => {
    it('should project multiple events in sequence', () => {
      // Arrange
      const aggregate = createEmptyAggregate(
        partitionKeys,
        'Account',
        'AccountProjector',
        1
      )
      
      const events = [
        createEvent({
          partitionKeys,
          aggregateType: 'Account',
          version: 1,
          payload: new AccountOpened('Alice', 100)
        }),
        createEvent({
          partitionKeys,
          aggregateType: 'Account',
          version: 2,
          payload: new MoneyDeposited(200)
        }),
        createEvent({
          partitionKeys,
          aggregateType: 'Account',
          version: 3,
          payload: new MoneyWithdrawn(50)
        })
      ]
      
      // Act
      const result = projector.projectEvents(aggregate, events)
      
      // Assert
      expect(result.version).toBe(3)
      expect(result.lastSortableUniqueId).toBe(events[2]!.id)
      
      const account = result.payload as Account
      expect(account.owner).toBe('Alice')
      expect(account.balance).toBe(250) // 100 + 200 - 50
    })
    
    it('should handle empty events array', () => {
      // Arrange
      const aggregate = new Aggregate(
        partitionKeys,
        'Account',
        5,
        new Account('Bob', 1000),
        SortableUniqueId.generate(),
        'AccountProjector',
        1
      )
      
      // Act
      const result = projector.projectEvents(aggregate, [])
      
      // Assert
      expect(result).toBe(aggregate) // Should return same instance
    })
  })
  
  describe('getInitialAggregate', () => {
    it('should create initial empty aggregate', () => {
      // Act
      const aggregate = projector.getInitialAggregate(partitionKeys, 'Account')
      
      // Assert
      expect(aggregate.version).toBe(0)
      expect(aggregate.partitionKeys).toEqual(partitionKeys)
      expect(aggregate.aggregateType).toBe('Account')
      expect(aggregate.payload).toBeInstanceOf(EmptyAggregatePayload)
      expect(aggregate.projectorTypeName).toBe('AccountProjector')
      expect(aggregate.projectorVersion).toBe(1)
    })
  })
  
  describe('error handling', () => {
    it('should handle projection errors gracefully', () => {
      // Arrange
      class ErrorProjector implements IProjector<IAggregatePayload> {
        getTypeName(): string { return 'ErrorProjector' }
        getVersion(): number { return 1 }
        project(payload: IAggregatePayload, event: IEventPayload): IAggregatePayload {
          if (event instanceof MoneyWithdrawn) {
            throw new Error('Projection error')
          }
          return payload
        }
      }
      
      const errorProjector = new AggregateProjector(new ErrorProjector())
      const aggregate = createEmptyAggregate(partitionKeys, 'Account', 'ErrorProjector', 1)
      
      const event = createEvent({
        partitionKeys,
        aggregateType: 'Account',
        version: 1,
        payload: new MoneyWithdrawn(100)
      })
      
      // Act & Assert
      expect(() => errorProjector.projectEvent(aggregate, event)).toThrow('Projection error')
    })
  })
})