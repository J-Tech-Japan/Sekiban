import { Result, ResultAsync, ok, err } from 'neverthrow';
import type { CommandDefinition } from './command-schema';
import type { ProjectorDefinitionType } from './projector-schema';
import type { SchemaRegistry } from './registry';
import type { IEvent } from '../events/event';
import { createEvent, createEventMetadata, type EventMetadata } from '../events/event';
import type { PartitionKeys } from '../documents/partition-keys';
import type { Aggregate } from '../aggregates/aggregate';
import type { EmptyAggregatePayload } from '../aggregates/aggregate';
import type { ITypedAggregatePayload } from '../aggregates/aggregate-projector';
import type { SekibanError } from '../result/errors';
import { ValidationError, CommandValidationError, QueryExecutionError } from '../result/errors';
import { SortableUniqueId } from '../documents/sortable-unique-id';
import { InMemoryEventStore, InMemoryEventReader, InMemoryEventWriter } from '../events/in-memory-event-store';
import type { IMultiProjectionQuery } from '../queries/query';
import type { ICommandContext } from './command-schema';
import type { IEventPayload } from '../events/event-payload';
import type { Metadata } from '../documents/metadata';

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
  async executeCommand<T extends CommandDefinition<any>>(
    commandDef: T,
    commandData: any
  ): Promise<Result<CommandResponse, SekibanError>> {
    try {
      // Validate command data against schema
      const validationResult = commandDef.validate(commandData);
      if (validationResult.isErr()) {
        return err(validationResult.error);
      }

      // Create command instance
      const commandInstance = commandDef.create(commandData);
      
      // Get partition keys
      const partitionKeys = commandInstance.specifyPartitionKeys(commandData);
      
      // Load current aggregate state
      const aggregate = await this.loadAggregate(partitionKeys, commandDef);
      
      // Create command context
      const context = this.createCommandContext(aggregate);
      
      // Execute command handler
      const eventsResult = commandInstance.handle(commandData, context);
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
        eventIds.push(sortableId.toString());
        
        const event = createEvent({
          id: sortableId,
          partitionKeys,
          aggregateType: commandDef.projector.aggregateTypeName || partitionKeys.group || 'Unknown',
          version: aggregate.version + events.length + 1,
          eventType: (eventPayload as any).type || eventPayload.constructor.name,
          payload: eventPayload,
          metadata: createEventMetadata({ timestamp: new Date() })
        });
        
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
    commandDef: CommandDefinition<any>
  ): Promise<Aggregate<TPayloadUnion | EmptyAggregatePayload>> {
    // Find the projector for this aggregate type
    const projectorDef = this.registry.getProjector(commandDef.projector.aggregateTypeName || partitionKeys.group || 'Unknown');
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
    let aggregate: any = projectorDef.getInitialState(partitionKeys);
    
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
      if (projectorDef) {
        const aggregate = await this.loadAggregateWithProjector(partitionKeys, projectorDef);
        
        return ok({
          data: aggregate as Aggregate<TPayloadUnion | EmptyAggregatePayload>,
          version: aggregate.version,
          lastEventId: aggregate.lastSortableUniqueId?.toString()
        });
      } else {
        // Fallback to registry lookup with 'any' type
        const projector = this.registry.getProjector(partitionKeys.group || 'Unknown');
        if (!projector) {
          return err(new QueryExecutionError(
            'queryAggregate',
            `No projector found for aggregate type: ${partitionKeys.group}`
          ));
        }

        const aggregate = await this.loadAggregateWithProjector(partitionKeys, projector);
        
        return ok({
          data: aggregate as Aggregate<TPayloadUnion | EmptyAggregatePayload>,
          version: aggregate.version,
          lastEventId: aggregate.lastSortableUniqueId?.toString()
        });
      }
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
        // MultiProjection queries need a different execution approach
        // For now, return an error
        return err(new QueryExecutionError(
          'queryMultiProjection',
          'MultiProjection queries not yet implemented in schema executor'
        ));
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
  private async loadAggregateWithProjector(
    partitionKeys: PartitionKeys,
    projectorDef: any
  ): Promise<Aggregate<ITypedAggregatePayload | EmptyAggregatePayload>> {
    // Load events for this aggregate
    const eventsResult = await this.eventReader.getEventsByPartitionKeys(partitionKeys);
    
    if (eventsResult.isErr()) {
      return projectorDef.getInitialState(partitionKeys);
    }

    // Project events to build current state
    let aggregate: Aggregate<ITypedAggregatePayload | EmptyAggregatePayload> = projectorDef.getInitialState(partitionKeys);
    
    for (const event of eventsResult.value) {
      const projectionResult = projectorDef.project(aggregate, event);
      if (projectionResult.isOk()) {
        aggregate = projectionResult.value;
      }
    }

    return aggregate;
  }

  /**
   * Create command context for handler execution
   */
  private createCommandContext<TAggregatePayload extends ITypedAggregatePayload | EmptyAggregatePayload>(
    aggregate: Aggregate<TAggregatePayload>
  ): ICommandContext<TAggregatePayload> {
    const events: IEvent[] = [];
    const metadata: Metadata = { timestamp: new Date() };
    
    return {
      originalSortableUniqueId: aggregate.lastSortableUniqueId?.toString() || '',
      events,
      partitionKeys: aggregate.partitionKeys,
      metadata,
      
      getPartitionKeys(): PartitionKeys {
        return aggregate.partitionKeys;
      },
      
      getNextVersion(): number {
        return aggregate.version + events.length + 1;
      },
      
      getCurrentVersion(): number {
        return aggregate.version + events.length;
      },
      
      appendEvent(eventPayload: IEventPayload): Result<IEvent, SekibanError> {
        const sortableId = SortableUniqueId.generate();
        const event = createEvent({
          id: sortableId,
          partitionKeys: aggregate.partitionKeys,
          aggregateType: aggregate.aggregateType,
          version: this.getNextVersion(),
          eventType: (eventPayload as any).type || eventPayload.constructor.name,
          payload: eventPayload,
          metadata: this.metadata as EventMetadata
        });
        
        events.push(event);
        return ok(event);
      },
      
      getService<T>(_serviceType: new (...args: any[]) => T): Result<T, SekibanError> {
        return err(new CommandValidationError('Service', ['Service resolution not implemented in schema executor']));
      },
      
      getAggregate(): Result<Aggregate<TAggregatePayload>, SekibanError> {
        return ok(aggregate);
      }
    };
  }
}