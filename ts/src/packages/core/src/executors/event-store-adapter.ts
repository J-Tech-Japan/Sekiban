import { Result, ResultAsync, ok, err, okAsync, errAsync } from 'neverthrow';
import type { IEventStore as IStorageEventStore } from '../storage/storage-provider';
import type { IEventStore as IEventsEventStore, EventStoreOptions } from '../events/store';
import { Event, EventFilter, IEventPayload } from '../events/index';
import { EventStoreError, ConcurrencyError } from '../result/index';
import { PartitionKeys, Metadata, SortableUniqueId } from '../documents/index';
import { EventRetrievalInfo } from '../events/event-retrieval-info';
import { InMemoryEventStore as StorageInMemoryEventStore } from '../storage/in-memory-event-store';
import { StorageError } from '../storage/storage-provider';
import { createEventMetadata, createEvent } from '../events/event';
import { OptionalValue, SortableIdCondition, AggregateGroupStream } from '../events/index';
import { IEvent } from '../events/event';

/**
 * Adapter that provides the events IEventStore interface
 * while internally using a storage IEventStore
 */
export class EventStoreAdapter implements IEventsEventStore {
  private storageEventStore: IStorageEventStore;
  private eventsByAggregate = new Map<string, Event[]>();
  private versionsByAggregate = new Map<string, number>();
  private snapshotsByAggregate = new Map<string, Map<number, any>>();

  constructor(options: EventStoreOptions = {}) {
    // Create the storage event store
    this.storageEventStore = new StorageInMemoryEventStore({
      type: 'InMemory' as any,
      enableLogging: false
    });
  }

  /**
   * Appends events to the store
   */
  async appendEvents(
    partitionKeys: PartitionKeys,
    aggregateType: string,
    events: IEventPayload[],
    expectedVersion: number,
    metadata?: Partial<Metadata>
  ): Promise<Result<Event[], EventStoreError | ConcurrencyError>> {
    const key = this.getAggregateKey(partitionKeys, aggregateType);
    const currentVersion = this.versionsByAggregate.get(key) || 0;

    if (currentVersion !== expectedVersion) {
      return err(new ConcurrencyError(expectedVersion, currentVersion));
    }

    const storedEvents: Event[] = [];
    let version = currentVersion;

    for (const payload of events) {
      version++;
      const event = createEvent({
        id: SortableUniqueId.create(),
        partitionKeys,
        aggregateType,
        eventType: payload.constructor.name,
        version,
        payload,
        metadata: metadata ? createEventMetadata(metadata) : createEventMetadata()
      });
      storedEvents.push(event);
    }

    // Store events in memory
    const existingEvents = this.eventsByAggregate.get(key) || [];
    this.eventsByAggregate.set(key, [...existingEvents, ...storedEvents]);
    this.versionsByAggregate.set(key, version);

    // Note: In a real implementation, we would also save to the underlying storage
    // but for now this adapter is just an in-memory implementation

    return ok(storedEvents);
  }

  /**
   * Gets events for an aggregate
   */
  async getEvents(
    partitionKeys: PartitionKeys,
    aggregateType: string,
    fromVersion?: number
  ): Promise<Result<Event[], EventStoreError>> {
    const key = this.getAggregateKey(partitionKeys, aggregateType);
    const events = this.eventsByAggregate.get(key) || [];
    
    if (fromVersion !== undefined) {
      return ok(events.filter(e => e.version > fromVersion));
    }
    
    return ok(events);
  }

  /**
   * Gets the current version of an aggregate
   */
  async getAggregateVersion(
    partitionKeys: PartitionKeys,
    aggregateType: string
  ): Promise<Result<number, EventStoreError>> {
    const key = this.getAggregateKey(partitionKeys, aggregateType);
    return ok(this.versionsByAggregate.get(key) || 0);
  }

  /**
   * Queries events based on filter criteria
   */
  async queryEvents(
    filter: EventFilter,
    limit?: number,
    offset = 0
  ): Promise<Result<Event[], EventStoreError>> {
    const allEvents: Event[] = [];
    
    for (const events of this.eventsByAggregate.values()) {
      allEvents.push(...events);
    }

    let filtered = allEvents.filter(event => this.matchesFilter(event, filter));
    
    // Sort by timestamp and unique ID
    filtered.sort((a, b) => SortableUniqueId.compare(a.id, b.id));
    
    // Apply pagination
    if (offset > 0) {
      filtered = filtered.slice(offset);
    }
    if (limit) {
      filtered = filtered.slice(0, limit);
    }

    return ok(filtered);
  }

  /**
   * Gets a snapshot of an aggregate at a specific version
   */
  async getSnapshot<TSnapshot>(
    partitionKeys: PartitionKeys,
    aggregateType: string,
    version: number
  ): Promise<Result<TSnapshot | null, EventStoreError>> {
    const key = this.getAggregateKey(partitionKeys, aggregateType);
    const aggregateSnapshots = this.snapshotsByAggregate.get(key);
    
    if (!aggregateSnapshots) {
      return ok(null);
    }

    // Find the latest snapshot at or before the requested version
    let latestSnapshot: TSnapshot | null = null;
    let latestVersion = 0;

    for (const [snapVersion, snapshot] of aggregateSnapshots.entries()) {
      if (snapVersion <= version && snapVersion > latestVersion) {
        latestSnapshot = snapshot;
        latestVersion = snapVersion;
      }
    }

    return ok(latestSnapshot);
  }

  /**
   * Saves a snapshot of an aggregate
   */
  async saveSnapshot<TSnapshot>(
    partitionKeys: PartitionKeys,
    aggregateType: string,
    version: number,
    snapshot: TSnapshot
  ): Promise<Result<void, EventStoreError>> {
    const key = this.getAggregateKey(partitionKeys, aggregateType);
    
    if (!this.snapshotsByAggregate.has(key)) {
      this.snapshotsByAggregate.set(key, new Map());
    }
    
    this.snapshotsByAggregate.get(key)!.set(version, snapshot);
    return ok(undefined);
  }

  private getAggregateKey(partitionKeys: PartitionKeys, aggregateType: string): string {
    return `${aggregateType}:${PartitionKeys.toCompositeKey(partitionKeys)}`;
  }

  private matchesFilter(event: Event, filter: EventFilter): boolean {
    if (filter.aggregateId && event.partitionKeys.aggregateId !== filter.aggregateId) {
      return false;
    }
    
    if (filter.aggregateType && event.aggregateType !== filter.aggregateType) {
      return false;
    }
    
    if (filter.eventTypes && !filter.eventTypes.includes(event.eventType)) {
      return false;
    }
    
    if (filter.fromVersion !== undefined && event.version < filter.fromVersion) {
      return false;
    }
    
    if (filter.toVersion !== undefined && event.version > filter.toVersion) {
      return false;
    }
    
    if (filter.fromTimestamp && event.metadata.timestamp < filter.fromTimestamp) {
      return false;
    }
    
    if (filter.toTimestamp && event.metadata.timestamp > filter.toTimestamp) {
      return false;
    }
    
    return true;
  }
}