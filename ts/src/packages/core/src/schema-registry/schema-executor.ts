import { Result, ResultAsync, ok, err } from 'neverthrow';
import type { CommandDefinitionType } from './command-schema.js';
import type { ProjectorDefinitionType } from './projector-schema.js';
import type { SchemaRegistry } from './registry.js';
import type { IEvent } from '../events/event.js';
import type { PartitionKeys } from '../documents/partition-keys.js';
import type { Aggregate } from '../aggregates/aggregate.js';
import type { EmptyAggregatePayload } from '../aggregates/aggregate.js';
import type { ITypedAggregatePayload } from '../aggregates/aggregate-projector.js';
import type { SekibanError } from '../result/errors.js';
import { ValidationError, CommandValidationError, QueryExecutionError } from '../result/errors.js';
import { SortableUniqueId } from '../documents/sortable-unique-id.js';
import { InMemoryEventStore, InMemoryEventReader, InMemoryEventWriter } from '../events/in-memory-event-store.js';
import type { IMultiProjectionQuery } from '../queries/query.js';

/**
 * Configuration for schema-based executor
 */
export interface SchemaExecutorConfig {
  registry: SchemaRegistry;
  eventStore: InMemoryEventStore;
}

/**
 * Response from command execution
 */
export interface CommandResponse {
  success: boolean;
  aggregateId: string;
  version: number;
  eventIds: string[];
  error?: string;
}

/**
 * Query response structure
 */
export interface QueryResponse<T = any> {
  data: T;
  version: number;
  lastEventId?: string;
}

/**
 * Schema-aware executor that uses the schema registry for domain operations
 * 
 * This executor provides:
 * - Command execution with schema validation
 * - Aggregate querying with projection
 * - Multi-projection queries for cross-aggregate data
 * - Full type safety with runtime validation
 */
export class SchemaExecutor {
  private readonly registry: SchemaRegistry;
  private readonly eventStore: InMemoryEventStore;
  private readonly eventReader: InMemoryEventReader;
  private readonly eventWriter: InMemoryEventWriter;

  constructor(config: SchemaExecutorConfig) {
    this.registry = config.registry;
    this.eventStore = config.eventStore;
    this.eventReader = new InMemoryEventReader(config.eventStore);
    this.eventWriter = new InMemoryEventWriter(config.eventStore);
  }

  /**
   * Execute a schema-based command
   */
  async executeCommand<T extends CommandDefinitionType<any, any>>(
    commandDef: T,
    commandData: any
  ): Promise<Result<CommandResponse, SekibanError>> {
    try {
      // Validate command data against schema
      const validationResult = commandDef.validate(commandData);
      if (validationResult.isErr()) {
        return err(validationResult.error);
      }

      // Get partition keys
      const partitionKeys = commandDef.handlers.specifyPartitionKeys(commandData);
      
      // Load current aggregate state
      const aggregate = await this.loadAggregate(partitionKeys, commandDef);
      
      // Execute command handler
      const eventsResult = commandDef.handlers.handle(commandData, aggregate);
      if (eventsResult.isErr()) {
        return err(eventsResult.error);
      }

      // Handle no events case
      if (eventsResult.value.length === 0) {
        return ok({
          success: true,
          aggregateId: partitionKeys.aggregateId,
          version: aggregate.version,
          eventIds: []
        });
      }

      // Create IEvent objects
      const events: IEvent[] = [];
      const eventIds: string[] = [];
      
      for (const eventPayload of eventsResult.value) {
        const sortableId = SortableUniqueId.generate();
        eventIds.push(sortableId);
        
        const event: IEvent = {
          id: sortableId,
          partitionKeys,
          aggregateType: partitionKeys.group,
          version: aggregate.version + events.length + 1,
          eventType: eventPayload.type || eventPayload.constructor.name,
          eventVersion: 1,
          payload: eventPayload,
          metadata: {},
          sortableUniqueId: sortableId
        };
        
        events.push(event);
      }
      
      // Save events
      const saveResult = await this.eventWriter.appendEvents(events);
      if (saveResult.isErr()) {
        return err(saveResult.error);
      }

      return ok({
        success: true,
        aggregateId: partitionKeys.aggregateId,
        version: aggregate.version + events.length,
        eventIds
      });
    } catch (error) {
      return err(new CommandValidationError(
        commandDef.type,
        [error instanceof Error ? error.message : 'Unknown error']
      ));
    }
  }

  /**
   * Load aggregate state using projector
   */
  private async loadAggregate<TPayloadUnion extends ITypedAggregatePayload>(
    partitionKeys: PartitionKeys,
    commandDef: CommandDefinitionType<any, TPayloadUnion>
  ): Promise<Aggregate<TPayloadUnion | EmptyAggregatePayload>> {
    // Find the projector for this aggregate type
    const projectorDef = this.registry.getProjector(partitionKeys.group);
    if (!projectorDef) {
      // Create a default projector if none found
      const defaultProjector = {
        getInitialState: (pk: PartitionKeys) => ({
          partitionKeys: pk,
          aggregateType: pk.group,
          version: 0,
          payload: { aggregateType: 'Empty' as const },
          lastSortableUniqueId: null,
          projectorTypeName: pk.group,
          projectorVersion: 1
        })
      };
      return defaultProjector.getInitialState(partitionKeys) as any;
    }

    // Load events for this aggregate
    const eventsResult = await this.eventReader.getEventsByPartitionKeys(
      partitionKeys
    );
    
    if (eventsResult.isErr()) {
      return projectorDef.getInitialState(partitionKeys);
    }

    // Project events to build current state
    let aggregate = projectorDef.getInitialState(partitionKeys);
    
    for (const event of eventsResult.value) {
      const projectionResult = projectorDef.project(aggregate, event);
      if (projectionResult.isOk()) {
        aggregate = projectionResult.value;
      }
    }

    return aggregate;
  }

  /**
   * Query aggregate state
   */
  async queryAggregate<TPayloadUnion extends ITypedAggregatePayload>(
    partitionKeys: PartitionKeys,
    projectorDef?: ProjectorDefinitionType<TPayloadUnion>
  ): Promise<Result<QueryResponse<Aggregate<TPayloadUnion | EmptyAggregatePayload>>, QueryExecutionError>> {
    try {
      // Use provided projector or look it up from registry
      const projector = projectorDef || this.registry.getProjector(partitionKeys.group);
      if (!projector) {
        return err(new QueryExecutionError(
          'queryAggregate',
          `No projector found for aggregate type: ${partitionKeys.group}`
        ));
      }

      const aggregate = await this.loadAggregateWithProjector(partitionKeys, projector);
      
      return ok({
        data: aggregate,
        version: aggregate.version,
        lastEventId: aggregate.lastSortableUniqueId?.toString()
      });
    } catch (error) {
      return err(new QueryExecutionError(
        'queryAggregate',
        error instanceof Error ? error.message : 'Unknown error'
      ));
    }
  }

  /**
   * Execute a multi-projection query
   */
  async executeMultiProjectionQuery<TResult>(
    query: IMultiProjectionQuery<any, any, TResult>
  ): Promise<Result<QueryResponse<TResult>, QueryExecutionError>> {
    try {
      // Get all events based on query criteria
      const events = await this.loadEventsForQuery(query);
      
      // Execute the query with the events
      try {
        const result = await query.query(events);
        
        return ok({
          data: result,
          version: events.length,
          lastEventId: events[events.length - 1]?.sortableUniqueId
        });
      } catch (queryError) {
        throw queryError;
      }
    } catch (error) {
      return err(new QueryExecutionError(
        query.constructor.name,
        error instanceof Error ? error.message : 'Unknown error'
      ));
    }
  }

  /**
   * Load events for a multi-projection query
   */
  private async loadEventsForQuery(query: IMultiProjectionQuery<any, any, any>): Promise<IEvent[]> {
    // Simple implementation - load all events
    // In production, this would filter based on query criteria
    return this.eventStore.getAllEvents();
  }

  /**
   * Load aggregate with specific projector
   */
  private async loadAggregateWithProjector<TPayloadUnion extends ITypedAggregatePayload>(
    partitionKeys: PartitionKeys,
    projectorDef: ProjectorDefinitionType<TPayloadUnion>
  ): Promise<Aggregate<TPayloadUnion | EmptyAggregatePayload>> {
    // Load events for this aggregate
    const eventsResult = await this.eventReader.getEventsByPartitionKeys(partitionKeys);
    
    if (eventsResult.isErr()) {
      return projectorDef.getInitialState(partitionKeys);
    }

    // Project events to build current state
    let aggregate = projectorDef.getInitialState(partitionKeys);
    
    for (const event of eventsResult.value) {
      const projectionResult = projectorDef.project(aggregate, event);
      if (projectionResult.isOk()) {
        aggregate = projectionResult.value;
      }
    }

    return aggregate;
  }
}