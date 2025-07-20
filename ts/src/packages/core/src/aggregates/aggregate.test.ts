import { describe, it, expect } from 'vitest'
import {
  IAggregate,
  Aggregate,
  createEmptyAggregate,
  isEmptyAggregate
} from './aggregate'
import { PartitionKeys } from '../documents/partition-keys'
import { SortableUniqueId } from '../documents/sortable-unique-id'
import { IAggregatePayload } from './aggregate-payload'

// Test payload types
class UserPayload implements IAggregatePayload {
  constructor(
    public readonly name: string,
    public readonly email: string,
    public readonly isActive: boolean = true
  ) {}
}

class EmptyUserPayload implements IAggregatePayload {}

describe('Aggregate', () => {
  describe('IAggregate interface', () => {
    it('should have required properties', () => {
      // Arrange
      const partitionKeys = PartitionKeys.create('user-123', 'users')
      const payload = new UserPayload('John Doe', 'john@example.com')
      
      // Act
      const aggregate: IAggregate<UserPayload> = new Aggregate(
        partitionKeys,
        'User',
        1,
        payload,
        SortableUniqueId.generate(),
        'UserProjector',
        1
      )
      
      // Assert
      expect(aggregate.partitionKeys).toBe(partitionKeys)
      expect(aggregate.aggregateType).toBe('User')
      expect(aggregate.version).toBe(1)
      expect(aggregate.payload).toBe(payload)
      expect(aggregate.lastSortableUniqueId).toBeDefined()
      expect(aggregate.projectorTypeName).toBe('UserProjector')
      expect(aggregate.projectorVersion).toBe(1)
    })
  })
  
  describe('Aggregate class', () => {
    it('should create aggregate with all properties', () => {
      // Arrange
      const partitionKeys = PartitionKeys.create('order-456', 'orders')
      const payload = new UserPayload('Jane', 'jane@example.com')
      const lastEventId = SortableUniqueId.generate()
      
      // Act
      const aggregate = new Aggregate(
        partitionKeys,
        'Order',
        5,
        payload,
        lastEventId,
        'OrderProjector',
        2
      )
      
      // Assert
      expect(aggregate.partitionKeys).toEqual(partitionKeys)
      expect(aggregate.aggregateType).toBe('Order')
      expect(aggregate.version).toBe(5)
      expect(aggregate.payload).toEqual(payload)
      expect(aggregate.lastSortableUniqueId).toBe(lastEventId)
      expect(aggregate.projectorTypeName).toBe('OrderProjector')
      expect(aggregate.projectorVersion).toBe(2)
    })
    
    it('should be immutable', () => {
      // Arrange
      const aggregate = new Aggregate(
        PartitionKeys.create('test-1'),
        'Test',
        1,
        new UserPayload('Test', 'test@test.com'),
        SortableUniqueId.generate(),
        'TestProjector',
        1
      )
      
      // Act & Assert
      expect(() => {
        (aggregate as any).version = 2
      }).toThrow()
      
      expect(() => {
        (aggregate as any).payload = new UserPayload('Changed', 'changed@test.com')
      }).toThrow()
    })
    
    it('should get payload type name', () => {
      // Arrange
      const aggregate = new Aggregate(
        PartitionKeys.create('test-1'),
        'Test',
        1,
        new UserPayload('Test', 'test@test.com'),
        SortableUniqueId.generate(),
        'TestProjector',
        1
      )
      
      // Act
      const typeName = aggregate.payloadTypeName
      
      // Assert
      expect(typeName).toBe('UserPayload')
    })
  })
  
  describe('Empty Aggregate', () => {
    it('should create empty aggregate', () => {
      // Arrange
      const partitionKeys = PartitionKeys.create('new-123', 'users')
      
      // Act
      const aggregate = createEmptyAggregate(
        partitionKeys,
        'User',
        'UserProjector',
        1
      )
      
      // Assert
      expect(aggregate.version).toBe(0)
      expect(aggregate.partitionKeys).toEqual(partitionKeys)
      expect(aggregate.aggregateType).toBe('User')
      expect(aggregate.projectorTypeName).toBe('UserProjector')
      expect(aggregate.projectorVersion).toBe(1)
      expect(isEmptyAggregate(aggregate)).toBe(true)
    })
    
    it('should identify empty aggregate', () => {
      // Arrange
      const emptyAggregate = createEmptyAggregate(
        PartitionKeys.create('empty-1'),
        'Empty',
        'EmptyProjector',
        1
      )
      
      const nonEmptyAggregate = new Aggregate(
        PartitionKeys.create('full-1'),
        'Full',
        1,
        new UserPayload('User', 'user@test.com'),
        SortableUniqueId.generate(),
        'FullProjector',
        1
      )
      
      // Act & Assert
      expect(isEmptyAggregate(emptyAggregate)).toBe(true)
      expect(isEmptyAggregate(nonEmptyAggregate)).toBe(false)
    })
  })
  
  describe('withNewVersion', () => {
    it('should create new aggregate with updated version and payload', () => {
      // Arrange
      const original = new Aggregate(
        PartitionKeys.create('user-1', 'users'),
        'User',
        1,
        new UserPayload('John', 'john@test.com'),
        SortableUniqueId.generate(),
        'UserProjector',
        1
      )
      
      const newPayload = new UserPayload('John Updated', 'john.updated@test.com')
      const newEventId = SortableUniqueId.generate()
      
      // Act
      const updated = original.withNewVersion(newPayload, 2, newEventId)
      
      // Assert
      expect(updated).not.toBe(original) // New instance
      expect(updated.version).toBe(2)
      expect(updated.payload).toBe(newPayload)
      expect(updated.lastSortableUniqueId).toBe(newEventId)
      
      // Other properties remain the same
      expect(updated.partitionKeys).toEqual(original.partitionKeys)
      expect(updated.aggregateType).toBe(original.aggregateType)
      expect(updated.projectorTypeName).toBe(original.projectorTypeName)
      expect(updated.projectorVersion).toBe(original.projectorVersion)
    })
  })
  
  describe('Type safety', () => {
    it('should maintain payload type safety', () => {
      // This test verifies TypeScript compilation
      const aggregate: IAggregate<UserPayload> = new Aggregate(
        PartitionKeys.create('user-1'),
        'User',
        1,
        new UserPayload('Type', 'safe@test.com'),
        SortableUniqueId.generate(),
        'UserProjector',
        1
      )
      
      // TypeScript should allow accessing UserPayload properties
      expect(aggregate.payload.name).toBe('Type')
      expect(aggregate.payload.email).toBe('safe@test.com')
      expect(aggregate.payload.isActive).toBe(true)
    })
  })
})