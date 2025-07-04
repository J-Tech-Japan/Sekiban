import { Result } from 'neverthrow';
import { Event, EventFilter, IEventPayload } from './types.js';
import { EventStoreError, ConcurrencyError } from '../result/index.js';
import { PartitionKeys } from '../documents/index.js';

/**
 * Interface for event store implementations
 */
export interface IEventStore {
  /**
   * Appends events to the store
   */
  appendEvents(
    partitionKeys: PartitionKeys,
    aggregateType: string,
    events: IEventPayload[],
    expectedVersion: number,
    metadata?: Partial<import('../documents').Metadata>
  ): Promise<Result<Event[], EventStoreError | ConcurrencyError>>;
  
  /**
   * Gets events for an aggregate
   */
  getEvents(
    partitionKeys: PartitionKeys,
    aggregateType: string,
    fromVersion?: number
  ): Promise<Result<Event[], EventStoreError>>;
  
  /**
   * Gets the current version of an aggregate
   */
  getAggregateVersion(
    partitionKeys: PartitionKeys,
    aggregateType: string
  ): Promise<Result<number, EventStoreError>>;
  
  /**
   * Queries events based on filter criteria
   */
  queryEvents(
    filter: EventFilter,
    limit?: number,
    offset?: number
  ): Promise<Result<Event[], EventStoreError>>;
  
  /**
   * Gets a snapshot of an aggregate at a specific version
   */
  getSnapshot<TSnapshot>(
    partitionKeys: PartitionKeys,
    aggregateType: string,
    version: number
  ): Promise<Result<TSnapshot | null, EventStoreError>>;
  
  /**
   * Saves a snapshot of an aggregate
   */
  saveSnapshot<TSnapshot>(
    partitionKeys: PartitionKeys,
    aggregateType: string,
    version: number,
    snapshot: TSnapshot
  ): Promise<Result<void, EventStoreError>>;
}

/**
 * Options for event store configuration
 */
export interface EventStoreOptions {
  /**
   * Enable snapshots
   */
  enableSnapshots?: boolean;
  
  /**
   * Snapshot frequency (every N events)
   */
  snapshotFrequency?: number;
  
  /**
   * Maximum events to load at once
   */
  maxEventsPerLoad?: number;
  
  /**
   * Enable event compression
   */
  enableCompression?: boolean;
}

/**
 * Event store statistics
 */
export interface EventStoreStats {
  /**
   * Total number of events
   */
  totalEvents: number;
  
  /**
   * Total number of aggregates
   */
  totalAggregates: number;
  
  /**
   * Total number of snapshots
   */
  totalSnapshots: number;
  
  /**
   * Storage size in bytes
   */
  storageSizeBytes?: number;
}