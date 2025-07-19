import { describe, it, expect } from 'vitest';
import { AggregateBuilder } from './aggregate-builder';
import { 
  IAggregatePayload, 
  Aggregate,
  PartitionKeys,
  SortableUniqueId 
} from '@sekiban/core';

// Test aggregate payload
interface UserAggregate extends IAggregatePayload {
  userId: string;
  name: string;
  email: string;
  isActive: boolean;
  createdAt: Date;
  updatedAt?: Date;
}

describe('AggregateBuilder', () => {
  describe('Basic Aggregate Building', () => {
    it('should build a simple aggregate with defaults', () => {
      const builder = new AggregateBuilder<UserAggregate>('UserProjector');
      const partitionKeys = PartitionKeys.create('user-123', 'users');
      
      const aggregate = builder
        .withPartitionKeys(partitionKeys)
        .withPayload({
          userId: 'user-123',
          name: 'John Doe',
          email: 'john@example.com',
          isActive: true,
          createdAt: new Date()
        })
        .build();
      
      expect(aggregate.projectorName).toBe('UserProjector');
      expect(aggregate.partitionKeys).toBe(partitionKeys);
      expect(aggregate.version).toBe(1);
      expect(aggregate.lastEventId).toBeDefined();
      expect(aggregate.lastUpdated).toBeInstanceOf(Date);
      expect(aggregate.payload.userId).toBe('user-123');
    });

    it('should allow customizing all aggregate properties', () => {
      const partitionKeys = PartitionKeys.create('user-123', 'users');
      const lastEventId = SortableUniqueId.generate();
      const lastUpdated = new Date('2024-01-01T00:00:00Z');
      
      const aggregate = new AggregateBuilder<UserAggregate>('UserProjector')
        .withPartitionKeys(partitionKeys)
        .withPayload({
          userId: 'user-123',
          name: 'John Doe',
          email: 'john@example.com',
          isActive: false,
          createdAt: new Date('2023-01-01T00:00:00Z')
        })
        .withVersion(10)
        .withLastEventId(lastEventId)
        .withLastUpdated(lastUpdated)
        .build();
      
      expect(aggregate.version).toBe(10);
      expect(aggregate.lastEventId).toBe(lastEventId);
      expect(aggregate.lastUpdated).toBe(lastUpdated);
      expect(aggregate.payload.isActive).toBe(false);
    });
  });

  describe('Aggregate State Transitions', () => {
    it('should build aggregate at different versions', () => {
      const partitionKeys = PartitionKeys.create('user-123', 'users');
      const builder = new AggregateBuilder<UserAggregate>('UserProjector')
        .withPartitionKeys(partitionKeys);
      
      // Initial state
      const v1 = builder
        .withPayload({
          userId: 'user-123',
          name: 'John',
          email: 'john@example.com',
          isActive: true,
          createdAt: new Date()
        })
        .withVersion(1)
        .build();
      
      // After update
      const v2 = builder
        .withPayload({
          userId: 'user-123',
          name: 'John Doe',
          email: 'john@example.com',
          isActive: true,
          createdAt: v1.payload.createdAt,
          updatedAt: new Date()
        })
        .withVersion(2)
        .build();
      
      expect(v1.version).toBe(1);
      expect(v2.version).toBe(2);
      expect(v1.payload.name).toBe('John');
      expect(v2.payload.name).toBe('John Doe');
      expect(v2.payload.updatedAt).toBeDefined();
    });
  });

  describe('Copy and Evolve', () => {
    it('should create evolved version from existing aggregate', () => {
      const original = new AggregateBuilder<UserAggregate>('UserProjector')
        .withPartitionKeys(PartitionKeys.create('user-123', 'users'))
        .withPayload({
          userId: 'user-123',
          name: 'John',
          email: 'john@example.com',
          isActive: true,
          createdAt: new Date('2023-01-01')
        })
        .withVersion(5)
        .build();
      
      const evolved = AggregateBuilder.from(original)
        .withVersion(6)
        .updatePayload({ 
          name: 'John Doe',
          updatedAt: new Date()
        })
        .withLastEventId(SortableUniqueId.generate())
        .withLastUpdated(new Date())
        .build();
      
      expect(evolved.projectorName).toBe(original.projectorName);
      expect(evolved.partitionKeys).toBe(original.partitionKeys);
      expect(evolved.version).toBe(6);
      expect(evolved.payload.name).toBe('John Doe');
      expect(evolved.payload.email).toBe(original.payload.email);
      expect(evolved.payload.updatedAt).toBeDefined();
      expect(evolved.lastEventId).not.toBe(original.lastEventId);
    });
  });

  describe('Test Scenarios', () => {
    it('should build aggregate with snapshot data', () => {
      const partitionKeys = PartitionKeys.create('user-123', 'users');
      
      const snapshotAggregate = new AggregateBuilder<UserAggregate>('UserProjector')
        .withPartitionKeys(partitionKeys)
        .withPayload({
          userId: 'user-123',
          name: 'Snapshot User',
          email: 'snapshot@example.com',
          isActive: true,
          createdAt: new Date('2023-01-01')
        })
        .withVersion(50)
        .asSnapshot()
        .build();
      
      expect(snapshotAggregate.version).toBe(50);
      expect(snapshotAggregate.payload.name).toBe('Snapshot User');
      // Should have special metadata indicating it's from snapshot
      expect(snapshotAggregate.lastUpdated).toBeInstanceOf(Date);
    });

    it('should build empty/initial aggregate', () => {
      const partitionKeys = PartitionKeys.create('user-new', 'users');
      
      const emptyAggregate = new AggregateBuilder<UserAggregate>('UserProjector')
        .withPartitionKeys(partitionKeys)
        .asEmpty()
        .build();
      
      expect(emptyAggregate.version).toBe(0);
      expect(emptyAggregate.payload).toBeUndefined();
    });
  });

  describe('Validation', () => {
    it('should throw error when building without partition keys', () => {
      const builder = new AggregateBuilder<UserAggregate>('UserProjector')
        .withPayload({
          userId: 'user-123',
          name: 'John',
          email: 'john@example.com',
          isActive: true,
          createdAt: new Date()
        });
      
      expect(() => builder.build()).toThrow('PartitionKeys are required');
    });

    it('should throw error for invalid version', () => {
      const builder = new AggregateBuilder<UserAggregate>('UserProjector')
        .withPartitionKeys(PartitionKeys.create('user-123', 'users'))
        .withPayload({
          userId: 'user-123',
          name: 'John',
          email: 'john@example.com',
          isActive: true,
          createdAt: new Date()
        });
      
      expect(() => builder.withVersion(-1).build()).toThrow('Version must be non-negative');
    });
  });

  describe('Fluent API', () => {
    it('should support complete method chaining', () => {
      const partitionKeys = PartitionKeys.create('user-123', 'users');
      const lastEventId = SortableUniqueId.generate();
      
      const aggregate = new AggregateBuilder<UserAggregate>('UserProjector')
        .withPartitionKeys(partitionKeys)
        .withPayload({
          userId: 'user-123',
          name: 'John',
          email: 'john@example.com',
          isActive: true,
          createdAt: new Date()
        })
        .withVersion(5)
        .withLastEventId(lastEventId)
        .withLastUpdated(new Date())
        .build();
      
      expect(aggregate.projectorName).toBe('UserProjector');
      expect(aggregate.partitionKeys).toBe(partitionKeys);
      expect(aggregate.version).toBe(5);
      expect(aggregate.lastEventId).toBe(lastEventId);
    });
  });
});