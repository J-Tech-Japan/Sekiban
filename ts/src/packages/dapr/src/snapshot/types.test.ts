import { describe, it, expect } from 'vitest';
import type { AggregateSnapshot, SnapshotMetadata } from './types';
import type { ITypedAggregatePayload } from '@sekiban/core';
import { PartitionKeys } from '@sekiban/core';

// Test payload type
interface UserPayload extends ITypedAggregatePayload {
  name: string;
  email: string;
  version: number;
}

describe('Snapshot Types', () => {
  describe('AggregateSnapshot', () => {
    it('should create a valid snapshot with all required fields', () => {
      const partitionKeys = PartitionKeys.generate('UserAggregate');
      const payload: UserPayload = {
        name: 'John Doe',
        email: 'john@example.com',
        version: 1,
      };

      const snapshot: AggregateSnapshot<UserPayload> = {
        aggregateId: partitionKeys.aggregateId,
        partitionKey: partitionKeys,
        payload,
        version: 5,
        lastEventId: 'event-123',
        lastEventTimestamp: new Date('2024-01-01T00:00:00Z'),
        snapshotTimestamp: new Date('2024-01-01T00:00:01Z'),
      };

      expect(snapshot.aggregateId).toBe(partitionKeys.aggregateId);
      expect(snapshot.partitionKey).toBe(partitionKeys);
      expect(snapshot.payload).toEqual(payload);
      expect(snapshot.version).toBe(5);
      expect(snapshot.lastEventId).toBe('event-123');
      expect(snapshot.lastEventTimestamp).toEqual(new Date('2024-01-01T00:00:00Z'));
      expect(snapshot.snapshotTimestamp).toEqual(new Date('2024-01-01T00:00:01Z'));
    });

    it('should serialize and deserialize correctly', () => {
      const partitionKeys = PartitionKeys.generate('UserAggregate');
      const snapshot: AggregateSnapshot<UserPayload> = {
        aggregateId: partitionKeys.aggregateId,
        partitionKey: partitionKeys,
        payload: {
          name: 'Jane Doe',
          email: 'jane@example.com',
          version: 2,
        },
        version: 10,
        lastEventId: 'event-456',
        lastEventTimestamp: new Date('2024-01-02T00:00:00Z'),
        snapshotTimestamp: new Date('2024-01-02T00:00:01Z'),
      };

      const json = JSON.stringify(snapshot);
      const deserialized = JSON.parse(json);

      // Dates need to be converted back from strings
      deserialized.lastEventTimestamp = new Date(deserialized.lastEventTimestamp);
      deserialized.snapshotTimestamp = new Date(deserialized.snapshotTimestamp);

      expect(deserialized).toEqual(snapshot);
    });
  });

  describe('SnapshotMetadata', () => {
    it('should create valid metadata', () => {
      const metadata: SnapshotMetadata = {
        version: 15,
        lastEventId: 'event-789',
        lastEventTimestamp: new Date('2024-01-03T00:00:00Z'),
        snapshotTimestamp: new Date('2024-01-03T00:00:01Z'),
        eventCount: 15,
        compressed: false,
      };

      expect(metadata.version).toBe(15);
      expect(metadata.lastEventId).toBe('event-789');
      expect(metadata.eventCount).toBe(15);
      expect(metadata.compressed).toBe(false);
    });

    it('should handle compressed metadata', () => {
      const metadata: SnapshotMetadata = {
        version: 20,
        lastEventId: 'event-abc',
        lastEventTimestamp: new Date('2024-01-04T00:00:00Z'),
        snapshotTimestamp: new Date('2024-01-04T00:00:01Z'),
        eventCount: 20,
        compressed: true,
        compressionAlgorithm: 'gzip',
        compressedSize: 1024,
        uncompressedSize: 4096,
      };

      expect(metadata.compressed).toBe(true);
      expect(metadata.compressionAlgorithm).toBe('gzip');
      expect(metadata.compressedSize).toBe(1024);
      expect(metadata.uncompressedSize).toBe(4096);
    });
  });

  describe('Snapshot versioning', () => {
    it('should validate snapshot version compatibility', () => {
      const snapshot: AggregateSnapshot<UserPayload> = {
        aggregateId: 'user-123',
        partitionKey: PartitionKeys.existing('UserAggregate', 'user-123'),
        payload: {
          name: 'Test User',
          email: 'test@example.com',
          version: 1,
        },
        version: 5,
        lastEventId: 'event-xyz',
        lastEventTimestamp: new Date('2024-01-05T00:00:00Z'),
        snapshotTimestamp: new Date('2024-01-05T00:00:01Z'),
      };

      // Snapshot version should match the event count at the time
      expect(snapshot.version).toBeGreaterThan(0);
      expect(snapshot.lastEventTimestamp.getTime()).toBeLessThanOrEqual(
        snapshot.snapshotTimestamp.getTime()
      );
    });
  });
});