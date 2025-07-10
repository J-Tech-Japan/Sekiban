import type { ITypedAggregatePayload, PartitionKeys } from '@sekiban/core';

/**
 * Represents a snapshot of an aggregate at a specific point in time
 */
export interface AggregateSnapshot<TPayload extends ITypedAggregatePayload> {
  /** The unique identifier of the aggregate */
  aggregateId: string;
  
  /** The partition keys for the aggregate */
  partitionKey: PartitionKeys;
  
  /** The aggregate state at the time of the snapshot */
  payload: TPayload;
  
  /** The version number (event count) at snapshot time */
  version: number;
  
  /** The ID of the last event included in this snapshot */
  lastEventId: string;
  
  /** The timestamp of the last event included in this snapshot */
  lastEventTimestamp: Date;
  
  /** When this snapshot was created */
  snapshotTimestamp: Date;
}

/**
 * Metadata about a snapshot without the full payload
 */
export interface SnapshotMetadata {
  /** The version number (event count) at snapshot time */
  version: number;
  
  /** The ID of the last event included in this snapshot */
  lastEventId: string;
  
  /** The timestamp of the last event included in this snapshot */
  lastEventTimestamp: Date;
  
  /** When this snapshot was created */
  snapshotTimestamp: Date;
  
  /** Total number of events up to this snapshot */
  eventCount: number;
  
  /** Whether the snapshot payload is compressed */
  compressed: boolean;
  
  /** Compression algorithm used (if compressed) */
  compressionAlgorithm?: 'gzip' | 'lz4' | 'brotli';
  
  /** Size of compressed payload in bytes (if compressed) */
  compressedSize?: number;
  
  /** Original size in bytes (if compressed) */
  uncompressedSize?: number;
}

/**
 * Configuration for snapshot behavior
 */
export interface SnapshotConfiguration {
  /** The strategy to use for determining when to take snapshots */
  strategy: 'count' | 'time' | 'hybrid' | 'none';
  
  /** For count-based strategy: number of events between snapshots */
  countThreshold?: number;
  
  /** For time-based strategy: milliseconds between snapshots */
  timeIntervalMs?: number;
  
  /** Whether to compress snapshots */
  enableCompression?: boolean;
  
  /** Compression algorithm to use */
  compressionAlgorithm?: 'gzip' | 'lz4' | 'brotli';
  
  /** Minimum payload size in bytes before compression is applied */
  compressionThreshold?: number;
}

/**
 * Result of loading an aggregate with snapshot optimization
 */
export interface SnapshotLoadResult<TPayload extends ITypedAggregatePayload> {
  /** The current aggregate state */
  payload: TPayload;
  
  /** Current version number */
  version: number;
  
  /** Whether a snapshot was used */
  fromSnapshot: boolean;
  
  /** Number of events replayed after snapshot */
  eventsReplayed: number;
  
  /** Time taken to load in milliseconds */
  loadTimeMs: number;
}

/**
 * Snapshot-related errors
 */
export class SnapshotError extends Error {
  constructor(message: string, public readonly code: SnapshotErrorCode) {
    super(message);
    this.name = 'SnapshotError';
  }
}

export enum SnapshotErrorCode {
  SERIALIZATION_FAILED = 'SNAPSHOT_SERIALIZATION_FAILED',
  DESERIALIZATION_FAILED = 'SNAPSHOT_DESERIALIZATION_FAILED',
  COMPRESSION_FAILED = 'SNAPSHOT_COMPRESSION_FAILED',
  DECOMPRESSION_FAILED = 'SNAPSHOT_DECOMPRESSION_FAILED',
  VERSION_MISMATCH = 'SNAPSHOT_VERSION_MISMATCH',
  CORRUPTED_DATA = 'SNAPSHOT_CORRUPTED_DATA',
  STORAGE_ERROR = 'SNAPSHOT_STORAGE_ERROR',
}