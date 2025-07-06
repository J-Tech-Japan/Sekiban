import { Result, ok, err } from 'neverthrow';
import { SekibanExecutorBase, SimpleTransaction } from './base';
import { ISekibanTransaction, SekibanExecutorConfig } from './types';
import { CommandHandlerRegistry, ICommandExecutor, CommandContext, CommandResult, CommandExecutionOptions } from '../commands/index.js';
import { QueryHandlerRegistry } from '../queries/index.js';
import { 
  InMemoryEventStream,
  Event,
  IEventPayload,
  EventFilter
} from '../events/index.js';
// Use the in-memory event store implementation
import { EventsInMemoryStore } from './events-in-memory-store.js';
import type { IEventStore } from '../events/store.js';
import { 
  IAggregateLoader, 
  IAggregateProjector,
  IProjector,
  Aggregate,
  IAggregatePayload,
  ITypedAggregatePayload 
} from '../aggregates/index.js';
import { PartitionKeys, Metadata, SortableUniqueId } from '../documents/index.js';
import { EventStoreError, ConcurrencyError, SekibanError } from '../result/index.js';
import { ICommand } from '../commands/index.js';
import type { SekibanDomainTypes } from '../domain-types/interfaces.js';

/**
 * Domain-aware aggregate loader that uses SekibanDomainTypes
 */
export class DomainAwareAggregateLoader implements IAggregateLoader {
  constructor(
    private eventStore: IEventStore,
    private domainTypes: SekibanDomainTypes
  ) {}

  async load<TPayload extends IAggregatePayload>(
    partitionKeys: PartitionKeys,
    aggregateType: string
  ): Promise<Aggregate<TPayload> | null> {
    // Get projector from domain types
    const projector = this.domainTypes.projectorTypes.getProjectorByAggregateType(aggregateType) as IProjector<TPayload> | undefined;
    if (!projector) {
      return null;
    }

    const eventsResult = await this.eventStore.getEvents(partitionKeys, aggregateType);
    if (eventsResult.isErr()) {
      throw eventsResult.error;
    }

    const events = eventsResult.value;
    if (events.length === 0) {
      return null;
    }

    // Get initial state and apply events one by one
    let aggregate = projector.getInitialState(partitionKeys) as any;
    
    for (const event of events) {
      const projectionResult = (projector as any).project(aggregate, event);
      if (projectionResult && projectionResult.isErr && projectionResult.isErr()) {
        throw projectionResult.error;
      }
      aggregate = projectionResult && projectionResult.value ? projectionResult.value : projectionResult;
    }
    
    return aggregate as Aggregate<TPayload>;
  }

  async loadMany<TPayload extends IAggregatePayload>(
    keys: Array<{ partitionKeys: PartitionKeys; aggregateType: string }>
  ): Promise<Array<Aggregate<TPayload> | null>> {
    const results: Array<Aggregate<TPayload> | null> = [];
    
    for (const { partitionKeys, aggregateType } of keys) {
      const aggregate = await this.load<TPayload>(partitionKeys, aggregateType);
      results.push(aggregate);
    }
    
    return results;
  }
}

/**
 * Domain-aware command executor that uses SekibanDomainTypes
 */
export class DomainAwareCommandExecutor implements ICommandExecutor {
  constructor(
    private handlerRegistry: CommandHandlerRegistry,
    private eventStore: IEventStore,
    private eventStream: InMemoryEventStream,
    private aggregateLoader: IAggregateLoader,
    private domainTypes: SekibanDomainTypes
  ) {}

  async execute<TCommand extends ICommand>(
    command: TCommand,
    context: CommandContext,
    options?: CommandExecutionOptions
  ): Promise<Result<CommandResult, SekibanError>> {
    // Simple implementation that returns success with no events
    return ok({
      aggregateId: context.partitionKeys.aggregateId,
      version: 0,
      events: [],
      metadata: context.metadata
    });
  }

  protected getInitialAggregate(
    partitionKeys: PartitionKeys,
    aggregateType: string
  ): any {
    const projector = this.domainTypes.projectorTypes.getProjectorByAggregateType(aggregateType);
    if (!projector) {
      throw new Error(`No projector registered for aggregate type: ${aggregateType}`);
    }
    return projector.getInitialState(partitionKeys);
  }
}

/**
 * In-memory Sekiban executor that uses SekibanDomainTypes
 * This is the new standard implementation that all executors should follow
 */
export class InMemorySekibanExecutorWithDomainTypes extends SekibanExecutorBase {
  constructor(
    private domainTypes: SekibanDomainTypes,
    config: SekibanExecutorConfig = {}
  ) {
    const eventStore = new EventsInMemoryStore(config);
    const eventStream = new InMemoryEventStream();
    const commandHandlerRegistry = new CommandHandlerRegistry();
    const queryHandlerRegistry = new QueryHandlerRegistry();
    
    // Create domain-aware aggregate loader
    const aggregateLoader = new DomainAwareAggregateLoader(eventStore, domainTypes);
    
    // Create domain-aware command executor
    const commandExecutor = new DomainAwareCommandExecutor(
      commandHandlerRegistry,
      eventStore,
      eventStream,
      aggregateLoader,
      domainTypes
    );

    super(
      commandExecutor,
      queryHandlerRegistry,
      eventStore,
      aggregateLoader,
      config
    );
  }

  protected getAggregateTypeForCommand(command: ICommand): string {
    // Check if command types has the new method
    const commandTypes = this.domainTypes.commandTypes as any;
    if (commandTypes.getAggregateTypeForCommand) {
      const aggregateType = commandTypes.getAggregateTypeForCommand(command.commandType);
      if (aggregateType) {
        return aggregateType;
      }
    }

    // Fallback: Use domain types to resolve command to aggregate type
    const commandType = this.domainTypes.commandTypes.getCommandTypeByName(command.commandType);
    if (!commandType) {
      throw new Error(`Unknown command type: ${command.commandType}`);
    }

    // Fallback: extract from command type name
    // e.g., "CreateUserCommand" -> "User"
    const match = command.commandType.match(/^(Create|Update|Delete)(.+)Command$/);
    if (match && match[2]) {
      return match[2];
    }

    throw new Error(`Cannot determine aggregate type for command: ${command.commandType}`);
  }

  /**
   * Creates a transaction
   */
  async beginTransaction(): Promise<ISekibanTransaction> {
    return new SimpleTransaction(this);
  }

  /**
   * Get the domain types used by this executor
   */
  getDomainTypes(): SekibanDomainTypes {
    return this.domainTypes;
  }

  /**
   * Execute a command with the unified executor pattern
   * This method supports ICommandWithHandler pattern
   */
  async execute<TCommand extends ICommand>(
    command: TCommand,
    options?: any
  ): Promise<Result<any, SekibanError>> {
    // Check if this is an ICommandWithHandler
    if ('getProjector' in command && 'specifyPartitionKeys' in command) {
      // Use unified executor pattern
      const unifiedExecutor = new (await import('../commands/unified-executor.js')).UnifiedCommandExecutor(
        this.eventStore,
        this.aggregateLoader
      );
      return unifiedExecutor.execute(command as any, options);
    }
    
    // Fall back to traditional command execution
    const partitionKeys = this.extractPartitionKeys(command);
    return this.executeCommand(command, partitionKeys, options);
  }

  /**
   * Extract partition keys from command (helper method)
   */
  private extractPartitionKeys(command: ICommand): PartitionKeys {
    // Try to get from command directly
    if ('partitionKeys' in command) {
      return (command as any).partitionKeys;
    }
    
    // Try to determine from command type
    const aggregateType = this.getAggregateTypeForCommand(command);
    
    // Check if command has aggregateId
    if ('aggregateId' in command) {
      return PartitionKeys.create((command as any).aggregateId, aggregateType);
    }
    
    // Generate new partition keys
    return PartitionKeys.create(SortableUniqueId.generate().toString(), aggregateType);
  }
}

/**
 * Builder for creating InMemorySekibanExecutor with domain types
 */
export class InMemorySekibanExecutorBuilder {
  private config: SekibanExecutorConfig = {};

  constructor(private domainTypes: SekibanDomainTypes) {}

  withConfig(config: SekibanExecutorConfig): this {
    this.config = { ...this.config, ...config };
    return this;
  }

  withSnapshotting(frequency: number): this {
    this.config.enableSnapshots = true;
    this.config.snapshotFrequency = frequency;
    return this;
  }

  build(): InMemorySekibanExecutorWithDomainTypes {
    return new InMemorySekibanExecutorWithDomainTypes(this.domainTypes, this.config);
  }
}

// Export factory function for convenience
export function createInMemorySekibanExecutor(
  domainTypes: SekibanDomainTypes,
  config?: SekibanExecutorConfig
): InMemorySekibanExecutorWithDomainTypes {
  return new InMemorySekibanExecutorWithDomainTypes(domainTypes, config);
}