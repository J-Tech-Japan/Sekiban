import { describe, it, expect, beforeEach } from 'vitest'
import { AggregateProjector } from './projector'
import { Event } from '../events/types'
import { EventDocument } from '../events/types'
import { SortableUniqueId } from '../documents/sortable-unique-id'
import { PartitionKeys } from '../documents/partition-keys'

// Test domain events
class AccountOpened implements Event {
  constructor(
    public readonly accountId: string,
    public readonly owner: string,
    public readonly initialBalance: number
  ) {}
}

class MoneyDeposited implements Event {
  constructor(
    public readonly accountId: string,
    public readonly amount: number
  ) {}
}

class MoneyWithdrawn implements Event {
  constructor(
    public readonly accountId: string,
    public readonly amount: number
  ) {}
}

// Test aggregate payload
interface AccountPayload {
  accountId: string
  owner: string
  balance: number
  isOpen: boolean
}

// Test projector implementation
class AccountProjector {
  getInitialState(): AccountPayload {
    return {
      accountId: '',
      owner: '',
      balance: 0,
      isOpen: false
    }
  }
  
  apply(state: AccountPayload, event: Event): AccountPayload {
    if (event instanceof AccountOpened) {
      return {
        accountId: event.accountId,
        owner: event.owner,
        balance: event.initialBalance,
        isOpen: true
      }
    }
    
    if (event instanceof MoneyDeposited) {
      return {
        ...state,
        balance: state.balance + event.amount
      }
    }
    
    if (event instanceof MoneyWithdrawn) {
      return {
        ...state,
        balance: state.balance - event.amount
      }
    }
    
    return state
  }
}

describe('AggregateProjector', () => {
  let projector: AggregateProjector<AccountPayload>
  let accountProjector: AccountProjector
  
  beforeEach(() => {
    accountProjector = new AccountProjector()
    projector = new AggregateProjector(accountProjector)
  })
  
  describe('construction', () => {
    it('should create projector with domain projector', () => {
      // Assert
      expect(projector).toBeDefined()
      expect(projector['domainProjector']).toBe(accountProjector)
    })
  })
  
  describe('getInitialAggregate', () => {
    it('should return initial aggregate with version 0', () => {
      // Arrange
      const partitionKeys = PartitionKeys.create('account-123')
      
      // Act
      const aggregate = projector.getInitialAggregate(partitionKeys)
      
      // Assert
      expect(aggregate.partitionKeys).toBe(partitionKeys)
      expect(aggregate.version).toBe(0)
      expect(aggregate.payload.accountId).toBe('')
      expect(aggregate.payload.balance).toBe(0)
      expect(aggregate.payload.isOpen).toBe(false)
    })
  })
  
  describe('projectFromEvents', () => {
    it('should project aggregate from single event', () => {
      // Arrange
      const partitionKeys = PartitionKeys.create('account-123')
      const event = new AccountOpened('account-123', 'John Doe', 1000)
      const eventDoc: EventDocument = {
        aggregateId: 'account-123',
        partitionKeys,
        version: 1,
        eventType: 'AccountOpened',
        payload: event,
        sortableUniqueId: SortableUniqueId.generate(),
        timestamp: new Date(),
        metadata: {}
      }
      
      // Act
      const aggregate = projector.projectFromEvents([eventDoc], partitionKeys)
      
      // Assert
      expect(aggregate.version).toBe(1)
      expect(aggregate.payload.accountId).toBe('account-123')
      expect(aggregate.payload.owner).toBe('John Doe')
      expect(aggregate.payload.balance).toBe(1000)
      expect(aggregate.payload.isOpen).toBe(true)
    })
    
    it('should project aggregate from multiple events', () => {
      // Arrange
      const partitionKeys = PartitionKeys.create('account-123')
      const events = [
        new AccountOpened('account-123', 'John Doe', 1000),
        new MoneyDeposited('account-123', 500),
        new MoneyWithdrawn('account-123', 200)
      ]
      
      const eventDocs: EventDocument[] = events.map((event, index) => ({
        aggregateId: 'account-123',
        partitionKeys,
        version: index + 1,
        eventType: event.constructor.name,
        payload: event,
        sortableUniqueId: SortableUniqueId.generate(),
        timestamp: new Date(),
        metadata: {}
      }))
      
      // Act
      const aggregate = projector.projectFromEvents(eventDocs, partitionKeys)
      
      // Assert
      expect(aggregate.version).toBe(3)
      expect(aggregate.payload.balance).toBe(1300) // 1000 + 500 - 200
      expect(aggregate.payload.isOpen).toBe(true)
    })
    
    it('should project from empty events array', () => {
      // Arrange
      const partitionKeys = PartitionKeys.create('account-123')
      
      // Act
      const aggregate = projector.projectFromEvents([], partitionKeys)
      
      // Assert
      expect(aggregate.version).toBe(0)
      expect(aggregate.payload.balance).toBe(0)
      expect(aggregate.payload.isOpen).toBe(false)
    })
  })
  
  describe('projectFromSnapshot', () => {
    it('should project from snapshot and subsequent events', () => {
      // Arrange
      const partitionKeys = PartitionKeys.create('account-123')
      const snapshotPayload: AccountPayload = {
        accountId: 'account-123',
        owner: 'John Doe',
        balance: 1000,
        isOpen: true
      }
      const snapshotVersion = 5
      
      const subsequentEvent = new MoneyDeposited('account-123', 300)
      const eventDoc: EventDocument = {
        aggregateId: 'account-123',
        partitionKeys,
        version: 6,
        eventType: 'MoneyDeposited',
        payload: subsequentEvent,
        sortableUniqueId: SortableUniqueId.generate(),
        timestamp: new Date(),
        metadata: {}
      }
      
      // Act
      const aggregate = projector.projectFromSnapshot(
        snapshotPayload,
        snapshotVersion,
        [eventDoc],
        partitionKeys
      )
      
      // Assert
      expect(aggregate.version).toBe(6)
      expect(aggregate.payload.balance).toBe(1300) // 1000 + 300
    })
    
    it('should project from snapshot without subsequent events', () => {
      // Arrange
      const partitionKeys = PartitionKeys.create('account-123')
      const snapshotPayload: AccountPayload = {
        accountId: 'account-123',
        owner: 'John Doe',
        balance: 1000,
        isOpen: true
      }
      const snapshotVersion = 5
      
      // Act
      const aggregate = projector.projectFromSnapshot(
        snapshotPayload,
        snapshotVersion,
        [],
        partitionKeys
      )
      
      // Assert
      expect(aggregate.version).toBe(5)
      expect(aggregate.payload.balance).toBe(1000)
    })
  })
  
  describe('event ordering', () => {
    it('should maintain correct event order during projection', () => {
      // Arrange
      const partitionKeys = PartitionKeys.create('account-123')
      const events = [
        new AccountOpened('account-123', 'John Doe', 0),
        new MoneyDeposited('account-123', 100),
        new MoneyDeposited('account-123', 50),
        new MoneyWithdrawn('account-123', 30),
        new MoneyDeposited('account-123', 20)
      ]
      
      const eventDocs: EventDocument[] = events.map((event, index) => ({
        aggregateId: 'account-123',
        partitionKeys,
        version: index + 1,
        eventType: event.constructor.name,
        payload: event,
        sortableUniqueId: SortableUniqueId.generate(),
        timestamp: new Date(),
        metadata: {}
      }))
      
      // Act
      const aggregate = projector.projectFromEvents(eventDocs, partitionKeys)
      
      // Assert
      expect(aggregate.version).toBe(5)
      expect(aggregate.payload.balance).toBe(140) // 0 + 100 + 50 - 30 + 20
    })
  })
})
