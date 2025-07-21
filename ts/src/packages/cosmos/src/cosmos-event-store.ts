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
  SortableUniqueId,
  PartitionKeys,
  SekibanDomainTypes,
  SchemaRegistry
} from '@sekiban/core';

/**
 * Type representing JSON-serializable values
 * Similar to C# JsonNode, this ensures the payload is JSON-compatible
 */
type JsonValue =
  | string
  | number
  | boolean
  | null
  | { [key: string]: JsonValue }
  | JsonValue[];

/**
 * EventDocument interface matching C# EventDocumentCommon
 * Uses JsonValue for payload to keep it in JSON form
 */
interface EventDocument {
  id: string;
  payload: JsonValue; // JSON payload, similar to JsonNode in C#
  sortableUniqueId: string;
  version: number;
  aggregateId: string;
  aggregateGroup: string;
  rootPartitionKey: string;
  payloadTypeName: string;
  timeStamp: string;
  partitionKey: string;
  metadata: {
    causationId: string;
    correlationId: string;
    executedUser: string;
  };
}

/**
 * CosmosDB implementation of IEventStore
 * Implements both IEventReader and IEventWriter interfaces
 */
export class CosmosEventStore implements IEventStore {
  private eventsContainer: Container | null = null;
  private domainTypes: SekibanDomainTypes | null = null;
  private registry: SchemaRegistry | null = null;

  constructor(
    private database: Database, 
    domainTypes?: SekibanDomainTypes,
    registry?: SchemaRegistry
  ) {
    this.domainTypes = domainTypes || null;
    this.registry = registry || null;
  }

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
            partitionKey: { 
              paths: ['/rootPartitionKey', '/aggregateGroup', '/partitionKey'],
              kind: 'MultiHash' as any // Azure SDK type definition doesn't include MultiHash yet
            }
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
      .query<EventDocument>(query)
      .fetchAll();

    // Transform Cosmos documents back to IEvent format
    const events = resources.map(doc => this.transformCosmosDocumentToEvent(doc));

    // Apply sortable ID conditions in memory since Cosmos doesn't support complex ID comparisons
    let filteredEvents = events;
    if (eventRetrievalInfo.sortableIdCondition) {
      filteredEvents = filteredEvents.filter(e => 
        !eventRetrievalInfo.sortableIdCondition.outsideOfRange(e.id)
      );
    }

    // Sort by sortable ID
    filteredEvents.sort((a, b) => SortableUniqueId.compare(a.id, b.id));

    return filteredEvents;
  }

  private transformCosmosDocumentToEvent(doc: EventDocument): IEvent {
    // Convert sortableUniqueId string back to SortableUniqueId object
    const sortableIdResult = SortableUniqueId.fromString(doc.sortableUniqueId);
    if (sortableIdResult.isErr()) {
      throw new Error(`Invalid sortableUniqueId: ${doc.sortableUniqueId}`);
    }
    const sortableId = sortableIdResult.value;

    // Deserialize payload if registry is available
    let deserializedPayload: any = doc.payload;
    if (this.registry) {
      try {
        // Try to deserialize using the schema registry
        const result = this.registry.safeDeserializeEvent(doc.payloadTypeName, doc.payload);
        if (result.success) {
          deserializedPayload = result.data;
        } else {
          // If deserialization fails, keep the raw payload
          console.warn(`Failed to deserialize event ${doc.payloadTypeName}:`, result.error);
        }
      } catch (error) {
        // If any error occurs, keep the raw payload
        console.warn(`Error deserializing event ${doc.payloadTypeName}:`, error);
      }
    }

    return {
      // Convert sortableUniqueId string back to SortableUniqueId object
      id: sortableId,
      
      // Reconstruct partition keys
      partitionKeys: PartitionKeys.create(
        doc.aggregateId,
        doc.aggregateGroup,
        doc.rootPartitionKey
      ),
      
      // Map fields back to IEvent structure
      aggregateType: doc.aggregateGroup.replace('Projector', ''), // Remove 'Projector' suffix if present
      eventType: doc.payloadTypeName, // Map payloadTypeName back to eventType
      version: doc.version,
      payload: deserializedPayload,
      
      // Reconstruct metadata
      metadata: {
        timestamp: new Date(doc.timeStamp),
        correlationId: doc.metadata.correlationId,
        causationId: doc.metadata.causationId,
        userId: doc.metadata.executedUser,
        executedUser: doc.metadata.executedUser
      },
      
      // Compatibility fields
      aggregateId: doc.aggregateId,
      sortableUniqueId: sortableId,
      timestamp: new Date(doc.timeStamp),
      partitionKey: doc.partitionKey,
      aggregateGroup: doc.aggregateGroup,
      eventData: deserializedPayload
    };
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
      conditions.push('c.rootPartitionKey = @rootPartitionKey');
      parameters.push({
        name: '@rootPartitionKey',
        value: eventRetrievalInfo.rootPartitionKey.getValue()
      });
    }

    // Filter by aggregate stream (group)
    if (eventRetrievalInfo.aggregateStream.hasValueProperty) {
      const streamNames = eventRetrievalInfo.aggregateStream.getValue().getStreamNames();
      if (streamNames.length === 1) {
        conditions.push('c.aggregateGroup = @group');
        parameters.push({
          name: '@group',
          value: streamNames[0]
        });
      } else if (streamNames.length > 1) {
        const placeholders = streamNames.map((_, i) => `@group${i}`).join(', ');
        conditions.push(`c.aggregateGroup IN (${placeholders})`);
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
      conditions.push('c.aggregateId = @aggregateId');
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
    query += ' ORDER BY c.sortableUniqueId ASC';

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
        // Ensure payload is JSON-serializable
        // If domain types are available, they should ensure proper serialization
        let serializedPayload: JsonValue = event.payload as JsonValue;
        if (this.domainTypes) {
          // Domain types can validate/transform the payload if needed
          // For now, we trust that the payload is already JSON-serializable
          serializedPayload = event.payload as JsonValue;
        }

        // Create document in the exact format required
        const cosmosDocument: EventDocument = {
          // Use aggregateId as UUID for id
          id: event.partitionKeys.aggregateId,
          
          // Payload as JSON (similar to JsonNode in C#)
          payload: serializedPayload,
          
          // SortableUniqueId as string (not object)
          sortableUniqueId: event.id.value,
          
          // Version
          version: event.version,
          
          // Aggregate fields
          aggregateId: event.partitionKeys.aggregateId,
          aggregateGroup: event.partitionKeys.group || 'default',
          rootPartitionKey: event.partitionKeys.rootPartitionKey || 'default',
          
          // Use payloadTypeName instead of eventType
          payloadTypeName: event.eventType,
          
          // Use timeStamp (capital S) instead of timestamp
          timeStamp: event.metadata.timestamp?.toISOString() || new Date().toISOString(),
          
          // Partition key with @ separator
          partitionKey: [
            event.partitionKeys.rootPartitionKey || 'default',
            event.partitionKeys.group || 'default',
            event.partitionKeys.aggregateId
          ].join('@'),
          
          // Metadata - only include the fields needed
          metadata: {
            causationId: event.metadata.causationId || '',
            correlationId: event.metadata.correlationId || '',
            executedUser: event.metadata.executedUser || event.metadata.userId || 'system'
          }
        };
        
        await this.eventsContainer.items.create(cosmosDocument);
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