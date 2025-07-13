import { Database, Container, SqlQuerySpec } from '@azure/cosmos';
import { ResultAsync, okAsync, errAsync } from 'neverthrow';
import {
  IEvent,
  IEventReader,
  IEventWriter,
  IEventStore,
  EventRetrievalInfo,
  StorageError,
  ConnectionError,
  SortableUniqueId
} from '@sekiban/core';

/**
 * CosmosDB implementation of IEventStore
 * Implements both IEventReader and IEventWriter interfaces
 */
export class CosmosEventStore implements IEventStore {
  private eventsContainer: Container | null = null;

  constructor(private database: Database) {}

  /**
   * Initialize the storage provider
   */
  initialize(): ResultAsync<void, StorageError> {
    return ResultAsync.fromPromise(
      (async () => {
        try {
          // Create events container if it doesn't exist
          const { container } = await this.database.containers.createIfNotExists({
            id: 'events',
            partitionKey: { paths: ['/partitionKey'] }
          });
          this.eventsContainer = container;
        } catch (error) {
          throw new ConnectionError(
            `Failed to initialize CosmosDB: ${error instanceof Error ? error.message : 'Unknown error'}`,
            error instanceof Error ? error : undefined
          );
        }
      })(),
      (error) => error instanceof StorageError ? error : new ConnectionError(
        `Failed to initialize CosmosDB: ${error instanceof Error ? error.message : 'Unknown error'}`,
        error instanceof Error ? error : undefined
      )
    );
  }

  /**
   * Get events based on retrieval information
   */
  getEvents(eventRetrievalInfo: EventRetrievalInfo): ResultAsync<readonly IEvent[], Error> {
    if (!this.eventsContainer) {
      return errAsync(new ConnectionError('Event store not initialized'));
    }

    return ResultAsync.fromPromise(
      this.doGetEvents(eventRetrievalInfo),
      (error) => new StorageError(
        `Failed to query events: ${error instanceof Error ? error.message : 'Unknown error'}`,
        'QUERY_FAILED',
        error instanceof Error ? error : undefined
      )
    );
  }

  private async doGetEvents(eventRetrievalInfo: EventRetrievalInfo): Promise<readonly IEvent[]> {
    const query = this.buildQuery(eventRetrievalInfo);
    const { resources } = await this.eventsContainer!.items
      .query<IEvent>(query)
      .fetchAll();

    // Apply sortable ID conditions in memory since Cosmos doesn't support complex ID comparisons
    let filteredEvents = resources;
    if (eventRetrievalInfo.sortableIdCondition) {
      filteredEvents = filteredEvents.filter(e => 
        !eventRetrievalInfo.sortableIdCondition.outsideOfRange(e.id)
      );
    }

    // Sort by sortable ID
    filteredEvents.sort((a, b) => SortableUniqueId.compare(a.id, b.id));

    return filteredEvents;
  }

  private buildQuery(eventRetrievalInfo: EventRetrievalInfo): SqlQuerySpec {
    const conditions: string[] = [];
    const parameters: any[] = [];

    // Always select from events container
    let query = 'SELECT';
    
    // Add TOP clause if max count is specified
    if (eventRetrievalInfo.maxCount.hasValueProperty) {
      query += ` TOP ${eventRetrievalInfo.maxCount.getValue()}`;
    }
    
    query += ' * FROM c';

    // Filter by root partition key
    if (eventRetrievalInfo.rootPartitionKey.hasValueProperty) {
      conditions.push('c.partitionKeys.rootPartitionKey = @rootPartitionKey');
      parameters.push({
        name: '@rootPartitionKey',
        value: eventRetrievalInfo.rootPartitionKey.getValue()
      });
    }

    // Filter by aggregate stream (group)
    if (eventRetrievalInfo.aggregateStream.hasValueProperty) {
      const streamNames = eventRetrievalInfo.aggregateStream.getValue().getStreamNames();
      if (streamNames.length === 1) {
        conditions.push('c.partitionKeys.group = @group');
        parameters.push({
          name: '@group',
          value: streamNames[0]
        });
      } else if (streamNames.length > 1) {
        const placeholders = streamNames.map((_, i) => `@group${i}`).join(', ');
        conditions.push(`c.partitionKeys.group IN (${placeholders})`);
        streamNames.forEach((name, i) => {
          parameters.push({
            name: `@group${i}`,
            value: name
          });
        });
      }
    }

    // Filter by aggregate ID
    if (eventRetrievalInfo.aggregateId.hasValueProperty) {
      conditions.push('c.partitionKeys.aggregateId = @aggregateId');
      parameters.push({
        name: '@aggregateId',
        value: eventRetrievalInfo.aggregateId.getValue()
      });
    }

    // Add WHERE clause if there are conditions
    if (conditions.length > 0) {
      query += ' WHERE ' + conditions.join(' AND ');
    }

    // Order by sortable ID
    query += ' ORDER BY c.id ASC';

    return {
      query,
      parameters
    };
  }

  /**
   * Save events to storage
   */
  async saveEvents<TEvent extends IEvent>(events: TEvent[]): Promise<void> {
    if (!this.eventsContainer) {
      throw new ConnectionError('Event store not initialized');
    }

    try {
      // Save events with proper partition key
      for (const event of events) {
        const eventWithPartitionKey = {
          ...event,
          partitionKey: event.partitionKeys.partitionKey
        };
        await this.eventsContainer.items.create(eventWithPartitionKey);
      }
    } catch (error) {
      throw new StorageError(
        `Failed to save events: ${error instanceof Error ? error.message : 'Unknown error'}`,
        'SAVE_FAILED',
        error instanceof Error ? error : undefined
      );
    }
  }

  /**
   * Close the storage provider
   */
  close(): ResultAsync<void, StorageError> {
    // Nothing to close for CosmosDB
    this.eventsContainer = null;
    return okAsync(undefined);
  }
}