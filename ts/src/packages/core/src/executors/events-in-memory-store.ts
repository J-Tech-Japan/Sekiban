import { Result, ok, err } from 'neverthrow';
import { Event, EventFilter, IEventPayload } from '../events/index';
import { EventStoreError, ConcurrencyError } from '../result/index';
import { PartitionKeys, Metadata, SortableUniqueId } from '../documents/index';
import { IEventStore, EventStoreOptions } from '../events/store';
import { createEvent, createEventMetadata } from '../events/event';

/**
 * In-memory implementation of the events IEventStore interface
 */
export class EventsInMemoryStore implements IEventStore {
  private events = new Map<string, Event[]>();
  private snapshots = new Map<string, Map<number, any>>();
  private versions = new Map<string, number>();

  constructor(private options: EventStoreOptions = {}) {}

  async appendEvents(
    partitionKeys: PartitionKeys,
    aggregateType: string,
    events: IEventPayload[],
    expectedVersion: number,
    metadata?: Partial<Metadata>
  ): Promise<Result<Event[], EventStoreError | ConcurrencyError>> {
    const key = this.getAggregateKey(partitionKeys, aggregateType);
    const currentVersion = this.versions.get(key) || 0;

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

    // Store events
    const existingEvents = this.events.get(key) || [];
    this.events.set(key, [...existingEvents, ...storedEvents]);
    this.versions.set(key, version);

    // Check if we should create a snapshot
    if (this.options.enableSnapshots && 
        this.options.snapshotFrequency && 
        version % this.options.snapshotFrequency === 0) {
      // Snapshot creation would happen here
    }

    return ok(storedEvents);
  }

  async getEvents(
    partitionKeys: PartitionKeys,
    aggregateType: string,
    fromVersion?: number
  ): Promise<Result<Event[], EventStoreError>> {
    const key = this.getAggregateKey(partitionKeys, aggregateType);
    const events = this.events.get(key) || [];
    
    if (fromVersion !== undefined) {
      return ok(events.filter(e => e.version > fromVersion));
    }
    
    return ok(events);
  }

  async getAggregateVersion(
    partitionKeys: PartitionKeys,
    aggregateType: string
  ): Promise<Result<number, EventStoreError>> {
    const key = this.getAggregateKey(partitionKeys, aggregateType);
    return ok(this.versions.get(key) || 0);
  }

  async queryEvents(
    filter: EventFilter,
    limit?: number,
    offset = 0
  ): Promise<Result<Event[], EventStoreError>> {
    const allEvents: Event[] = [];
    
    for (const events of this.events.values()) {
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

  async getSnapshot<TSnapshot>(
    partitionKeys: PartitionKeys,
    aggregateType: string,
    version: number
  ): Promise<Result<TSnapshot | null, EventStoreError>> {
    const key = this.getAggregateKey(partitionKeys, aggregateType);
    const aggregateSnapshots = this.snapshots.get(key);
    
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

  async saveSnapshot<TSnapshot>(
    partitionKeys: PartitionKeys,
    aggregateType: string,
    version: number,
    snapshot: TSnapshot
  ): Promise<Result<void, EventStoreError>> {
    const key = this.getAggregateKey(partitionKeys, aggregateType);
    
    if (!this.snapshots.has(key)) {
      this.snapshots.set(key, new Map());
    }
    
    this.snapshots.get(key)!.set(version, snapshot);
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