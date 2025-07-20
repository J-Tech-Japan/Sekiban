import { ResultAsync, okAsync, errAsync } from 'neverthrow';
import { IEvent } from '../events/event';
import { EventRetrievalInfo, OptionalValue } from '../events/event-retrieval-info';
import { IEventStore, StorageProviderConfig, StorageError } from './storage-provider';
import { SortableUniqueId } from '../documents/sortable-unique-id';

/**
 * In-memory event store for testing and development
 * Implements the powerful EventRetrievalInfo-based querying
 */
export class InMemoryEventStore implements IEventStore {
  private events: IEvent[] = [];
  private closed = false;

  constructor(private config: StorageProviderConfig) {}

  /**
   * Get events based on retrieval information
   */
  getEvents(eventRetrievalInfo: EventRetrievalInfo): ResultAsync<readonly IEvent[], Error> {
    if (this.closed) {
      return errAsync(new StorageError('Event store is closed', 'STORE_CLOSED'));
    }

    return ResultAsync.fromSafePromise(
      this.doGetEvents(eventRetrievalInfo)
    );
  }

  private async doGetEvents(eventRetrievalInfo: EventRetrievalInfo): Promise<readonly IEvent[]> {
    let filteredEvents = [...this.events];

    // Filter by root partition key if specified
    if (eventRetrievalInfo.rootPartitionKey.hasValueProperty) {
      const rootPartition = eventRetrievalInfo.rootPartitionKey.getValue();
      filteredEvents = filteredEvents.filter(e => 
        (e.partitionKeys.rootPartitionKey || 'default') === rootPartition
      );
    }

    // Filter by aggregate stream (group) if specified
    if (eventRetrievalInfo.aggregateStream.hasValueProperty) {
      const streamNames = eventRetrievalInfo.aggregateStream.getValue().getStreamNames();
      if (streamNames.length > 0) {
        filteredEvents = filteredEvents.filter(e => 
          streamNames.includes(e.partitionKeys.group || 'default')
        );
      }
    }

    // Filter by aggregate ID if specified
    if (eventRetrievalInfo.aggregateId.hasValueProperty) {
      const aggregateId = eventRetrievalInfo.aggregateId.getValue();
      filteredEvents = filteredEvents.filter(e => 
        e.partitionKeys.aggregateId === aggregateId
      );
    }

    // Apply sortable ID condition
    filteredEvents = filteredEvents.filter(e => 
      !eventRetrievalInfo.sortableIdCondition.outsideOfRange(e.id)
    );

    // Sort by sortable ID
    filteredEvents.sort((a, b) => SortableUniqueId.compare(a.id, b.id));

    // Apply max count limit if specified
    if (eventRetrievalInfo.maxCount.hasValueProperty) {
      const maxCount = eventRetrievalInfo.maxCount.getValue();
      filteredEvents = filteredEvents.slice(0, maxCount);
    }

    return filteredEvents;
  }

  /**
   * Initialize the storage provider
   */
  initialize(): ResultAsync<void, StorageError> {
    // Nothing to initialize for in-memory storage
    return okAsync(undefined);
  }

  /**
   * Close the storage provider
   */
  close(): ResultAsync<void, StorageError> {
    this.closed = true;
    this.events = [];
    return okAsync(undefined);
  }

  /**
   * Save events to storage
   */
  async saveEvents<TEvent extends IEvent>(events: TEvent[]): Promise<void> {
    if (this.closed) {
      throw new StorageError('Event store is closed', 'STORE_CLOSED');
    }

    // In-memory implementation doesn't need to check for conflicts
    // Just append the events
    this.events.push(...events);
  }
}