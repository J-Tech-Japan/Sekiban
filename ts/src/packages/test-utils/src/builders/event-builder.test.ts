import { describe, it, expect } from 'vitest';
import { EventBuilder } from './event-builder';
import { 
  IEventPayload, 
  EventDocument, 
  PartitionKeys,
  SortableUniqueId 
} from '@sekiban/core';

// Test event payloads
interface UserCreated extends IEventPayload {
  userId: string;
  name: string;
  email: string;
}

interface UserUpdated extends IEventPayload {
  userId: string;
  name?: string;
  email?: string;
}

describe('EventBuilder', () => {
  describe('Basic Event Building', () => {
    it('should build a simple event with defaults', () => {
      const builder = new EventBuilder<UserCreated>('UserCreated');
      
      const event = builder
        .withPayload({
          userId: 'user-123',
          name: 'John Doe',
          email: 'john@example.com'
        })
        .build();
      
      expect(event.eventType).toBe('UserCreated');
      expect(event.payload.userId).toBe('user-123');
      expect(event.payload.name).toBe('John Doe');
      expect(event.payload.email).toBe('john@example.com');
      expect(event.version).toBe(1);
      expect(event.timestamp).toBeInstanceOf(Date);
      expect(event.sortableUniqueId).toBeDefined();
    });

    it('should allow customizing all event properties', () => {
      const customTimestamp = new Date('2024-01-01T00:00:00Z');
      const customSortableId = SortableUniqueId.generate();
      const partitionKeys = PartitionKeys.create('aggregate-123', 'users');
      
      const builder = new EventBuilder<UserCreated>('UserCreated');
      
      const event = builder
        .withPayload({
          userId: 'user-123',
          name: 'John Doe',
          email: 'john@example.com'
        })
        .withVersion(5)
        .withTimestamp(customTimestamp)
        .withSortableUniqueId(customSortableId)
        .withPartitionKeys(partitionKeys)
        .withMetadata({ source: 'test' })
        .build();
      
      expect(event.version).toBe(5);
      expect(event.timestamp).toEqual(customTimestamp);
      expect(event.sortableUniqueId.toString()).toBe(customSortableId.toString());
      expect(event.partitionKeys).toBe(partitionKeys);
      expect(event.metadata).toEqual({ source: 'test' });
    });
  });

  describe('Fluent API', () => {
    it('should support method chaining', () => {
      const event = new EventBuilder<UserCreated>('UserCreated')
        .withPayload({ userId: 'user-123', name: 'John', email: 'john@example.com' })
        .withVersion(2)
        .withMetadata({ correlationId: 'corr-123' })
        .build();
      
      expect(event.eventType).toBe('UserCreated');
      expect(event.version).toBe(2);
      expect(event.metadata?.correlationId).toBe('corr-123');
    });

    it('should create independent instances', () => {
      const basePayload = { userId: 'user-123', name: 'John', email: 'john@example.com' };
      
      const builder1 = new EventBuilder<UserCreated>('UserCreated')
        .withPayload(basePayload)
        .withVersion(1);
      const event1 = builder1.build();
      
      const builder2 = new EventBuilder<UserCreated>('UserCreated')
        .withPayload(basePayload)
        .withVersion(2);
      const event2 = builder2.build();
      
      expect(event1.version).toBe(1);
      expect(event2.version).toBe(2);
      expect(event1.sortableUniqueId.toString()).not.toBe(event2.sortableUniqueId.toString());
    });
  });

  describe('Partial Payload Updates', () => {
    it('should support updating payload partially', () => {
      const builder = new EventBuilder<UserUpdated>('UserUpdated');
      
      const event = builder
        .withPayload({ userId: 'user-123' })
        .updatePayload({ name: 'Jane Doe' })
        .updatePayload({ email: 'jane@example.com' })
        .build();
      
      expect(event.payload.userId).toBe('user-123');
      expect(event.payload.name).toBe('Jane Doe');
      expect(event.payload.email).toBe('jane@example.com');
    });
  });

  describe('Batch Event Creation', () => {
    it('should create multiple events with incremental versions', () => {
      const builder = new EventBuilder<UserCreated>('UserCreated');
      const partitionKeys = PartitionKeys.create('aggregate-123', 'users');
      
      const events = builder
        .withPartitionKeys(partitionKeys)
        .buildMany([
          { userId: 'user-1', name: 'User 1', email: 'user1@example.com' },
          { userId: 'user-2', name: 'User 2', email: 'user2@example.com' },
          { userId: 'user-3', name: 'User 3', email: 'user3@example.com' }
        ]);
      
      expect(events).toHaveLength(3);
      expect(events[0].version).toBe(1);
      expect(events[1].version).toBe(2);
      expect(events[2].version).toBe(3);
      
      // All should have the same partition keys
      events.forEach(event => {
        expect(event.partitionKeys).toBe(partitionKeys);
      });
      
      // Each should have unique sortable IDs
      const sortableIds = events.map(e => e.sortableUniqueId);
      const uniqueIds = new Set(sortableIds);
      expect(uniqueIds.size).toBe(3);
    });

    it('should create events with custom starting version', () => {
      const builder = new EventBuilder<UserCreated>('UserCreated');
      
      const events = builder
        .withVersion(10)
        .buildMany([
          { userId: 'user-1', name: 'User 1', email: 'user1@example.com' },
          { userId: 'user-2', name: 'User 2', email: 'user2@example.com' }
        ]);
      
      expect(events[0].version).toBe(10);
      expect(events[1].version).toBe(11);
    });
  });

  describe('Event Sequences', () => {
    it('should build a sequence of different event types', () => {
      const partitionKeys = PartitionKeys.create('user-123', 'users');
      
      const createEvent = new EventBuilder<UserCreated>('UserCreated')
        .withPartitionKeys(partitionKeys)
        .withPayload({ userId: 'user-123', name: 'John', email: 'john@example.com' })
        .withVersion(1)
        .build();
      
      const updateEvent = new EventBuilder<UserUpdated>('UserUpdated')
        .withPartitionKeys(partitionKeys)
        .withPayload({ userId: 'user-123', name: 'John Doe' })
        .withVersion(2)
        .withTimestampAfter(createEvent.timestamp, 1000) // 1 second after
        .build();
      
      expect(updateEvent.timestamp.getTime()).toBeGreaterThan(createEvent.timestamp.getTime());
      expect(updateEvent.timestamp.getTime() - createEvent.timestamp.getTime()).toBe(1000);
    });
  });

  describe('Copy and Modify', () => {
    it('should create a copy of existing event with modifications', () => {
      const original = new EventBuilder<UserCreated>('UserCreated')
        .withPayload({ userId: 'user-123', name: 'John', email: 'john@example.com' })
        .withVersion(1)
        .build();
      
      const modified = EventBuilder.from(original)
        .withVersion(2)
        .updatePayload({ name: 'John Doe' })
        .build();
      
      expect(modified.eventType).toBe(original.eventType);
      expect(modified.payload.userId).toBe(original.payload.userId);
      expect(modified.payload.email).toBe(original.payload.email);
      expect(modified.payload.name).toBe('John Doe');
      expect(modified.version).toBe(2);
      expect(modified.sortableUniqueId).not.toBe(original.sortableUniqueId);
    });
  });

  describe('Validation', () => {
    it('should throw error when building without payload', () => {
      const builder = new EventBuilder<UserCreated>('UserCreated');
      
      expect(() => builder.build()).toThrow('Payload is required');
    });

    it('should throw error for invalid version', () => {
      const builder = new EventBuilder<UserCreated>('UserCreated')
        .withPayload({ userId: 'user-123', name: 'John', email: 'john@example.com' });
      
      expect(() => builder.withVersion(0).build()).toThrow('Version must be positive');
      expect(() => builder.withVersion(-1).build()).toThrow('Version must be positive');
    });
  });
});