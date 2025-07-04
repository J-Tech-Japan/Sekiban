import { Result, ok, err, ResultAsync } from 'neverthrow'
import { IEvent } from './event'
import { IEventPayload } from './event-payload'
import { PartitionKeys } from '../documents/partition-keys.js'
import { SortableUniqueId } from '../documents/sortable-unique-id.js'
import { EventStoreError, ConcurrencyError } from '../result/errors.js'
import { IAggregatePayload } from '../aggregates/aggregate-payload.js'

/**
 * Interface for reading events from the store
 */
export interface IEventReader {
  /**
   * Get events for a specific aggregate
   */
  getEventsByPartitionKeys(
    partitionKeys: PartitionKeys,
    fromVersion?: number
  ): ResultAsync<IEvent[], EventStoreError>
  
  /**
   * Get events by aggregate type
   */
  getEventsByAggregateType(
    aggregateType: string
  ): ResultAsync<IEvent[], EventStoreError>
  
  /**
   * Get all events (use with caution in production)
   */
  getAllEvents(): ResultAsync<IEvent[], EventStoreError>
  
  /**
   * Get events after a specific event ID
   */
  getEventsAfter(
    afterId: SortableUniqueId
  ): ResultAsync<IEvent[], EventStoreError>
  
  /**
   * Get latest snapshot for an aggregate (if any)
   */
  getLatestSnapshot<TPayload extends IAggregatePayload>(
    partitionKeys: PartitionKeys
  ): ResultAsync<IEvent<TPayload> | null, EventStoreError>
}

/**
 * Interface for writing events to the store
 */
export interface IEventWriter {
  /**
   * Append a single event
   */
  appendEvent<TPayload extends IEventPayload>(
    event: IEvent<TPayload>
  ): ResultAsync<IEvent<TPayload>, EventStoreError | ConcurrencyError>
  
  /**
   * Append multiple events atomically
   */
  appendEvents(
    events: IEvent[]
  ): ResultAsync<IEvent[], EventStoreError | ConcurrencyError>
}

/**
 * In-memory implementation of event store
 */
export class InMemoryEventStore {
  private events: Map<string, IEvent> = new Map()
  private eventsByPartition: Map<string, IEvent[]> = new Map()
  private eventsByType: Map<string, IEvent[]> = new Map()
  private allEventsList: IEvent[] = []
  private versionMap: Map<string, number> = new Map()
  
  /**
   * Add an event to the store
   */
  addEvent(event: IEvent): Result<IEvent, EventStoreError | ConcurrencyError> {
    const eventKey = event.id.toString()
    
    // Check for duplicate
    if (this.events.has(eventKey)) {
      return err(new EventStoreError('append', `Event with ID ${eventKey} already exists`))
    }
    
    // Check version consistency
    const partitionKey = event.partitionKeys.toString()
    const currentVersion = this.versionMap.get(partitionKey) || 0
    const expectedVersion = currentVersion + 1
    
    if (event.version !== expectedVersion) {
      return err(new ConcurrencyError(expectedVersion, event.version))
    }
    
    // Store the event
    this.events.set(eventKey, event)
    this.versionMap.set(partitionKey, event.version)
    
    // Update partition index
    if (!this.eventsByPartition.has(partitionKey)) {
      this.eventsByPartition.set(partitionKey, [])
    }
    this.eventsByPartition.get(partitionKey)!.push(event)
    
    // Update type index
    if (!this.eventsByType.has(event.aggregateType)) {
      this.eventsByType.set(event.aggregateType, [])
    }
    this.eventsByType.get(event.aggregateType)!.push(event)
    
    // Add to all events list
    this.allEventsList.push(event)
    
    return ok(event)
  }
  
  /**
   * Get events for a partition
   */
  getEventsByPartition(partitionKey: string, fromVersion?: number): IEvent[] {
    const events = this.eventsByPartition.get(partitionKey) || []
    
    if (fromVersion !== undefined) {
      return events.filter(e => e.version >= fromVersion)
    }
    
    return events.slice() // Return a copy
  }
  
  /**
   * Get events by type
   */
  getEventsByType(aggregateType: string): IEvent[] {
    return (this.eventsByType.get(aggregateType) || []).slice()
  }
  
  /**
   * Get all events
   */
  getAllEvents(): IEvent[] {
    return this.allEventsList.slice()
  }
  
  /**
   * Get events after a specific ID
   */
  getEventsAfter(afterId: string): IEvent[] {
    const afterIndex = this.allEventsList.findIndex(
      e => e.id.toString() === afterId
    )
    
    if (afterIndex === -1) {
      return this.allEventsList.slice()
    }
    
    return this.allEventsList.slice(afterIndex + 1)
  }
}

/**
 * In-memory event reader implementation
 */
export class InMemoryEventReader implements IEventReader {
  constructor(private store: InMemoryEventStore) {}
  
  getEventsByPartitionKeys(
    partitionKeys: PartitionKeys,
    fromVersion?: number
  ): ResultAsync<IEvent[], EventStoreError> {
    return ResultAsync.fromPromise(
      Promise.resolve(this.store.getEventsByPartition(partitionKeys.toString(), fromVersion)),
      () => new EventStoreError('read', 'Failed to read events')
    )
  }
  
  getEventsByAggregateType(
    aggregateType: string
  ): ResultAsync<IEvent[], EventStoreError> {
    return ResultAsync.fromPromise(
      Promise.resolve(this.store.getEventsByType(aggregateType)),
      () => new EventStoreError('read', 'Failed to read events by type')
    )
  }
  
  getAllEvents(): ResultAsync<IEvent[], EventStoreError> {
    return ResultAsync.fromPromise(
      Promise.resolve(this.store.getAllEvents()),
      () => new EventStoreError('read', 'Failed to read all events')
    )
  }
  
  getEventsAfter(
    afterId: SortableUniqueId
  ): ResultAsync<IEvent[], EventStoreError> {
    return ResultAsync.fromPromise(
      Promise.resolve(this.store.getEventsAfter(afterId.toString())),
      () => new EventStoreError('read', 'Failed to read events after ID')
    )
  }
  
  getLatestSnapshot<TPayload extends IAggregatePayload>(
    partitionKeys: PartitionKeys
  ): ResultAsync<IEvent<TPayload> | null, EventStoreError> {
    // In-memory store doesn't support snapshots yet
    return ResultAsync.fromPromise(
      Promise.resolve(null),
      () => new EventStoreError('read', 'Failed to read snapshot')
    )
  }
}

/**
 * In-memory event writer implementation
 */
export class InMemoryEventWriter implements IEventWriter {
  constructor(private store: InMemoryEventStore) {}
  
  appendEvent<TPayload extends IEventPayload>(
    event: IEvent<TPayload>
  ): ResultAsync<IEvent<TPayload>, EventStoreError | ConcurrencyError> {
    return ResultAsync.fromPromise(
      Promise.resolve(this.store.addEvent(event)),
      () => new EventStoreError('append', 'Failed to append event')
    ).andThen(result => 
      result.isOk() 
        ? ok(result.value as IEvent<TPayload>)
        : err(result.error)
    )
  }
  
  appendEvents(
    events: IEvent[]
  ): ResultAsync<IEvent[], EventStoreError | ConcurrencyError> {
    return ResultAsync.fromPromise(
      (async () => {
        const results: IEvent[] = []
        
        for (const event of events) {
          const result = this.store.addEvent(event)
          if (result.isErr()) {
            throw result.error
          }
          results.push(result.value)
        }
        
        return results
      })(),
      (error) => error as EventStoreError | ConcurrencyError
    )
  }
}