import { Result, ok, err } from 'neverthrow';
import type { 
  ICommandWithHandler,
  ICommandContext,
  ICommandContextWithoutState 
} from '../schema-registry/command-schema';
import type { IEventStore } from '../events/store';
import type { IAggregateLoader } from '../aggregates/loader';
import type { IAggregateProjector, ITypedAggregatePayload } from '../aggregates/aggregate-projector';
import type { IEvent, IEventPayload } from '../events/index';
import { createEvent, createEventMetadata, type EventMetadata } from '../events/event';
import type { Aggregate } from '../aggregates/aggregate';
import type { EmptyAggregatePayload } from '../aggregates/aggregate';
import type { SekibanError } from '../result/errors';
import { 
  CommandValidationError, 
  AggregateNotFoundError,
  ConcurrencyError 
} from '../result/errors';
import type { PartitionKeys, Metadata } from '../documents/index';
import { SortableUniqueId } from '../documents/sortable-unique-id';

/**
 * Command execution result
 */
export interface CommandExecutionResult {
  aggregateId: string;
  version: number;
  events: IEvent[];
  metadata: Metadata;
}

/**
 * Options for command execution
 */
export interface UnifiedCommandExecutionOptions {
  /**
   * Skip command validation
   */
  skipValidation?: boolean;
  
  /**
   * Custom metadata to merge with command metadata
   */
  metadata?: Partial<Metadata>;
  
  /**
   * Service provider for dependency injection
   */
  serviceProvider?: IServiceProvider;
}

/**
 * Service provider interface for dependency injection
 */
export interface IServiceProvider {
  getService<T>(serviceType: new (...args: any[]) => T): T | undefined;
}

/**
 * Unified command executor that works with ICommandWithHandler
 */
export class UnifiedCommandExecutor {
  constructor(
    private readonly eventStore: IEventStore,
    private readonly aggregateLoader: IAggregateLoader
  ) {}
  
  /**
   * Execute a command that implements ICommandWithHandler
   */
  async execute<
    TCommand,
    TProjector extends IAggregateProjector<TPayloadUnion>,
    TPayloadUnion extends ITypedAggregatePayload,
    TAggregatePayload extends TPayloadUnion | EmptyAggregatePayload = TPayloadUnion | EmptyAggregatePayload
  >(
    command: ICommandWithHandler<TCommand, TProjector, TPayloadUnion, TAggregatePayload>,
    options?: UnifiedCommandExecutionOptions
  ): Promise<Result<CommandExecutionResult, SekibanError>> {
    // Get projector and create context without state first
    const projector = command.getProjector();
    const contextWithoutState = this.createContextWithoutState(command, options);
    
    // Get partition keys from command
    const partitionKeys = command.specifyPartitionKeys(command as any);
    
    // Validate command if not skipped
    if (!options?.skipValidation) {
      const validationResult = command.validate(command as any);
      if (validationResult.isErr()) {
        return err(validationResult.error);
      }
    }
    
    // Load or create aggregate
    const aggregateResult = await this.loadOrCreateAggregate(
      partitionKeys, 
      projector,
      contextWithoutState
    );
    
    if (aggregateResult.isErr()) {
      return err(aggregateResult.error);
    }
    
    const aggregate = aggregateResult.value;
    
    // Create full context with aggregate
    const context = this.createContext(
      aggregate,
      contextWithoutState,
      options
    );
    
    // Handle command
    const eventsResult = command.handle(command as any, context as ICommandContext<TAggregatePayload>);
    if (eventsResult.isErr()) {
      return err(eventsResult.error);
    }
    
    const eventPayloads = eventsResult.value;
    
    // Build events
    const events = this.buildEvents(
      eventPayloads,
      aggregate,
      partitionKeys,
      options?.metadata || {}
    );
    
    // Store events
    if (events.length > 0) {
      const storeResult = await this.eventStore.appendEvents(
        partitionKeys,
        aggregate.aggregateType,
        events.map(e => e.payload),
        aggregate.version
      );
      
      if (storeResult.isErr()) {
        return err(storeResult.error);
      }
    }
    
    // Return result
    return ok({
      aggregateId: partitionKeys.aggregateId,
      version: aggregate.version + events.length,
      events,
      metadata: { timestamp: new Date(), ...options?.metadata }
    });
  }
  
  /**
   * Create context without aggregate state
   */
  private createContextWithoutState<TCommand>(
    command: ICommandWithHandler<TCommand, any, any, any>,
    options?: UnifiedCommandExecutionOptions
  ): ICommandContextWithoutState {
    const events: IEvent[] = [];
    const metadata = {
      timestamp: new Date(),
      ...options?.metadata
    };
    
    // We'll set partition keys after calling specifyPartitionKeys
    const partitionKeys = { aggregateId: '', group: '', rootPartitionKey: '' } as any;
    
    return {
      originalSortableUniqueId: '',
      events,
      partitionKeys,
      metadata,
      
      getPartitionKeys: () => partitionKeys,
      getNextVersion: () => events.length + 1,
      getCurrentVersion: () => events.length,
      
      appendEvent: (eventPayload: IEventPayload) => {
        const event = createEvent({
          id: SortableUniqueId.generate(),
          partitionKeys,
          aggregateType: command.getProjector().aggregateTypeName,
          eventType: eventPayload.constructor.name || 'UnknownEvent',
          version: events.length + 1,
          payload: eventPayload,
          metadata: metadata as EventMetadata
        });
        events.push(event);
        return ok(event);
      },
      
      getService: <T>(serviceType: new (...args: any[]) => T) => {
        if (options?.serviceProvider) {
          const service = options.serviceProvider.getService(serviceType);
          return service ? ok(service) : err(new CommandValidationError(
            command.commandType,
            [`Service ${serviceType.name} not found`]
          ));
        }
        return err(new CommandValidationError(
          command.commandType,
          ['No service provider configured']
        ));
      }
    };
  }
  
  /**
   * Create full context with aggregate
   */
  private createContext<TAggregatePayload extends ITypedAggregatePayload | EmptyAggregatePayload>(
    aggregate: Aggregate<TAggregatePayload>,
    baseContext: ICommandContextWithoutState,
    options?: UnifiedCommandExecutionOptions
  ): ICommandContext<TAggregatePayload> {
    // Update partition keys in base context
    (baseContext as any).partitionKeys = aggregate.partitionKeys;
    (baseContext as any).originalSortableUniqueId = aggregate.lastSortableUniqueId?.toString() || '';
    
    return {
      ...baseContext,
      
      getAggregate: () => ok(aggregate),
      
      // Override getNextVersion to include aggregate version
      getNextVersion: () => aggregate.version + baseContext.events.length + 1,
      getCurrentVersion: () => aggregate.version + baseContext.events.length
    };
  }
  
  /**
   * Load existing aggregate or create new one
   */
  private async loadOrCreateAggregate<
    TPayloadUnion extends ITypedAggregatePayload
  >(
    partitionKeys: PartitionKeys,
    projector: IAggregateProjector<TPayloadUnion>,
    context: ICommandContextWithoutState
  ): Promise<Result<Aggregate<TPayloadUnion | EmptyAggregatePayload>, SekibanError>> {
    // Check if this is a new aggregate
    const isNew = !partitionKeys.aggregateId || 
                  partitionKeys.aggregateId === 'new' ||
                  partitionKeys.aggregateId.startsWith('temp-');
    
    if (isNew) {
      // Return initial empty aggregate
      const initialAggregate = projector.getInitialState(partitionKeys);
      return ok(initialAggregate);
    }
    
    // Load existing aggregate
    const aggregateType = (projector as any).aggregateTypeName || (projector as any).aggregateType || partitionKeys.group;
    const loadResult = await this.aggregateLoader.load(
      partitionKeys,
      aggregateType
    );
    
    if (!loadResult) {
      // No events found - this is a new aggregate
      const initialAggregate = projector.getInitialState(partitionKeys);
      return ok(initialAggregate);
    }
    
    // If loadResult is already an aggregate, return it
    return ok(loadResult as Aggregate<TPayloadUnion | EmptyAggregatePayload>);
  }
  
  /**
   * Build events from event payloads
   */
  private buildEvents(
    eventPayloads: IEventPayload[],
    aggregate: Aggregate<any>,
    partitionKeys: PartitionKeys,
    metadata: Partial<Metadata>
  ): IEvent[] {
    const events: IEvent[] = [];
    let version = aggregate.version;
    
    for (const payload of eventPayloads) {
      version++;
      const event = createEvent({
        id: SortableUniqueId.generate(),
        partitionKeys,
        aggregateType: aggregate.aggregateType,
        eventType: (payload as any).type || payload.constructor.name || 'UnknownEvent',
        version,
        payload,
        metadata: createEventMetadata({
          timestamp: new Date(),
          ...metadata
        })
      });
      events.push(event);
    }
    
    return events;
  }
}

/**
 * Create a unified command executor instance
 */
export function createUnifiedExecutor(
  eventStore: IEventStore,
  aggregateLoader: IAggregateLoader
): UnifiedCommandExecutor {
  return new UnifiedCommandExecutor(eventStore, aggregateLoader);
}