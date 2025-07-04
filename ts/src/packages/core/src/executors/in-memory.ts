import { Result, ok, err } from 'neverthrow';
import { SekibanExecutorBase, SimpleTransaction } from './base';
import { ISekibanTransaction, SekibanExecutorConfig } from './types';
import { CommandExecutorBase, CommandHandlerRegistry } from '../commands/index.js';
import { QueryHandlerRegistry } from '../queries/index.js';
import { 
  IEventStore, 
  EventStoreOptions, 
  InMemoryEventStream,
  Event,
  IEventPayload,
  EventFilter
} from '../events/index.js';
import { 
  IAggregateLoader, 
  IAggregateProjector,
  IProjector,
  Aggregate,
  IAggregatePayload,
  ITypedAggregatePayload 
} from '../aggregates/index.js';
import { PartitionKeys, Metadata, SortableUniqueId } from '../documents/index.js';
import { EventStoreError, ConcurrencyError } from '../result/index.js';
import { ICommand } from '../commands/index.js';

/**
 * In-memory implementation of event store
 */
export class InMemoryEventStore implements IEventStore {
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
      const event: Event = {
        id: SortableUniqueId.create(),
        partitionKeys,
        aggregateType,
        eventType: payload.constructor.name,
        version,
        payload,
        metadata: metadata ? Metadata.merge(Metadata.create(), metadata) : Metadata.create(),
      };
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

/**
 * In-memory aggregate loader
 */
export class InMemoryAggregateLoader implements IAggregateLoader {
  constructor(
    private eventStore: IEventStore,
    private projectorRegistry: Map<string, IProjector<any>>
  ) {}

  async load<TPayload extends IAggregatePayload>(
    partitionKeys: PartitionKeys,
    aggregateType: string
  ): Promise<Aggregate<TPayload> | null> {
    const projector = this.projectorRegistry.get(aggregateType);
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

    return projector.project(events, partitionKeys);
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
 * In-memory command executor
 */
export class InMemoryCommandExecutor extends CommandExecutorBase {
  constructor(
    handlerRegistry: CommandHandlerRegistry,
    eventStore: IEventStore,
    eventStream: InMemoryEventStream,
    aggregateLoader: IAggregateLoader,
    private projectorRegistry: Map<string, IProjector<any>>
  ) {
    super(handlerRegistry, eventStore, eventStream, aggregateLoader);
  }

  protected getInitialAggregate(
    partitionKeys: PartitionKeys,
    aggregateType: string
  ): any {
    const projector = this.projectorRegistry.get(aggregateType);
    if (!projector) {
      throw new Error(`No projector registered for aggregate type: ${aggregateType}`);
    }
    return projector.getInitialState(partitionKeys);
  }
}

/**
 * In-memory Sekiban executor
 */
export class InMemorySekibanExecutor extends SekibanExecutorBase {
  private projectorRegistry = new Map<string, IProjector<any>>();
  private commandTypeToAggregateType = new Map<string, string>();

  constructor(
    config: SekibanExecutorConfig = {}
  ) {
    const eventStore = new InMemoryEventStore(config);
    const eventStream = new InMemoryEventStream();
    const commandHandlerRegistry = new CommandHandlerRegistry();
    const queryHandlerRegistry = new QueryHandlerRegistry();
    const aggregateLoader = new InMemoryAggregateLoader(eventStore, new Map());
    
    const commandExecutor = new InMemoryCommandExecutor(
      commandHandlerRegistry,
      eventStore,
      eventStream,
      aggregateLoader,
      new Map()
    );

    super(
      commandExecutor,
      queryHandlerRegistry,
      eventStore,
      aggregateLoader,
      config
    );

    // Update the aggregate loader with the projector registry
    (aggregateLoader as any).projectorRegistry = this.projectorRegistry;
    (commandExecutor as any).projectorRegistry = this.projectorRegistry;
  }

  /**
   * Registers a projector
   */
  registerProjector<TPayload extends ITypedAggregatePayload>(
    projector: IProjector<TPayload>
  ): void {
    this.projectorRegistry.set(projector.getTypeName(), projector);
  }

  /**
   * Registers a command type to aggregate type mapping
   */
  registerCommandMapping(commandType: string, aggregateType: string): void {
    this.commandTypeToAggregateType.set(commandType, aggregateType);
  }

  protected getAggregateTypeForCommand(command: ICommand): string {
    const aggregateType = this.commandTypeToAggregateType.get(command.commandType);
    if (!aggregateType) {
      throw new Error(`No aggregate type mapping for command: ${command.commandType}`);
    }
    return aggregateType;
  }

  /**
   * Creates a transaction
   */
  async beginTransaction(): Promise<ISekibanTransaction> {
    return new SimpleTransaction(this);
  }
}

/**
 * Builder for in-memory Sekiban executor
 */
export class InMemorySekibanExecutorBuilder {
  private config: SekibanExecutorConfig = {};
  private commandHandlers: Array<{
    handler: any;
    commandType: string;
    aggregateType: string;
  }> = [];
  private queryHandlers: any[] = [];
  private projectors: IProjector<any>[] = [];

  withConfig(config: SekibanExecutorConfig): InMemorySekibanExecutorBuilder {
    this.config = { ...this.config, ...config };
    return this;
  }

  withCommandHandler(
    handler: any,
    commandType: string,
    aggregateType: string
  ): InMemorySekibanExecutorBuilder {
    this.commandHandlers.push({ handler, commandType, aggregateType });
    return this;
  }

  withQueryHandler(handler: any): InMemorySekibanExecutorBuilder {
    this.queryHandlers.push(handler);
    return this;
  }

  withProjector(projector: IProjector<any>): InMemorySekibanExecutorBuilder {
    this.projectors.push(projector);
    return this;
  }

  build(): InMemorySekibanExecutor {
    const executor = new InMemorySekibanExecutor(this.config);

    // Register projectors
    for (const projector of this.projectors) {
      executor.registerProjector(projector);
    }

    // Register command handlers and mappings
    for (const { handler, commandType, aggregateType } of this.commandHandlers) {
      (executor as any).commandExecutor.handlerRegistry.register(handler);
      executor.registerCommandMapping(commandType, aggregateType);
    }

    // Register query handlers
    for (const handler of this.queryHandlers) {
      (executor as any).queryHandlerRegistry.register(handler);
    }

    return executor;
  }
}