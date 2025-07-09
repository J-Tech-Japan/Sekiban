import { 
  SortableUniqueId, 
  Aggregate,
  PartitionKeys,
  EmptyAggregatePayload,
  AggregateProjector,
  SekibanError,
  IEvent,
  ICommandWithHandler,
  ITypedAggregatePayload,
  ICommandContext,
  Metadata
} from '@sekiban/core';
import { Result, ok, err } from 'neverthrow';
import { AbstractActor } from '@dapr/dapr';
import type { 
  IAggregateActor,
  IAggregateEventHandlerActor,
  SerializableAggregate,
  ActorSerializableCommandAndMetadata,
  SerializableEventDocument
} from './interfaces.js';
import { PartitionKeysAndProjector } from '../parts/partition-keys-and-projector.js';
import { DaprRepository } from '../parts/dapr-repository.js';
import type { SekibanDomainTypes } from '@sekiban/core';
import type { IActorProxyFactory } from '../types/index.js';


/**
 * Serializable partition info for state storage
 */
interface SerializedPartitionInfo {
  grainKey: string;
}

/**
 * Serialization service interface matching C#
 */
interface IDaprSerializationService {
  deserializeAggregateAsync(surrogate: any): Promise<Aggregate | null>;
  serializeAggregateAsync(aggregate: Aggregate): Promise<any>;
}

/**
 * Dapr actor for aggregate projection and command execution.
 * This is the Dapr equivalent of Orleans' AggregateProjectorGrain.
 */
export class AggregateActor extends AbstractActor implements IAggregateActor {
  private static readonly STATE_KEY = 'aggregateState';
  private static readonly PARTITION_INFO_KEY = 'partitionInfo';
  
  private currentAggregate: Aggregate = Aggregate.empty();
  private hasUnsavedChanges = false;
  private partitionInfo?: PartitionKeysAndProjector<any>;

  private domainTypes: SekibanDomainTypes;
  private serviceProvider: any;
  private actorProxyFactory: IActorProxyFactory;
  private serializationService: IDaprSerializationService;

  constructor(ctx: any, id: any) {
    super(ctx, id);
    // These will be injected via a factory or setup method
    this.domainTypes = {} as SekibanDomainTypes;
    this.serviceProvider = {};
    this.actorProxyFactory = {} as IActorProxyFactory;
    this.serializationService = {} as IDaprSerializationService;
  }

  // Method to inject dependencies after construction
  setupDependencies(
    domainTypes: SekibanDomainTypes,
    serviceProvider: any,
    actorProxyFactory: IActorProxyFactory,
    serializationService: IDaprSerializationService
  ): void {
    this.domainTypes = domainTypes;
    this.serviceProvider = serviceProvider;
    this.actorProxyFactory = actorProxyFactory;
    this.serializationService = serializationService;
  }

  async onActivate(): Promise<void> {
    await super.onActivate();
    console.log(`AggregateActor ${this.id} activated`);
    
    // Register timer for periodic state saving
    await this.registerTimer(
      'SaveState',
      'saveStateCallbackAsync',
      null,
      '10s',  // Due time
      '10s'   // Period
    );
  }

  async onDeactivate(): Promise<void> {
    if (this.hasUnsavedChanges) {
      await this.saveStateAsync();
    }
    await super.onDeactivate();
  }

  async getAggregateStateAsync(): Promise<SerializableAggregate> {
    const aggregate = await this.getStateInternalAsync();
    return {
      partitionKeys: aggregate.partitionKeys,
      aggregate: aggregate,
      lastSortableUniqueId: aggregate.lastSortableUniqueId?.toString() || ''
    };
  }

  async rebuildStateAsync(): Promise<SerializableAggregate> {
    const aggregate = await this.rebuildStateInternalAsync();
    return {
      partitionKeys: aggregate.partitionKeys,
      aggregate: aggregate,
      lastSortableUniqueId: aggregate.lastSortableUniqueId?.toString() || ''
    };
  }

  async executeCommandAsync<
    TCommand,
    TProjector extends AggregateProjector<TPayloadUnion>,
    TPayloadUnion extends ITypedAggregatePayload,
    TAggregatePayload extends TPayloadUnion | EmptyAggregatePayload = TPayloadUnion | EmptyAggregatePayload
  >(commandAndMetadata: ActorSerializableCommandAndMetadata): Promise<string> {
    try {
      await this.ensureInitializedAsync();
      
      if (!this.partitionInfo) {
        throw new Error('Partition info not initialized');
      }

      const eventHandlerActor = this.getEventHandlerActor();

      if (this.currentAggregate === Aggregate.empty()) {
        this.currentAggregate = await this.loadStateInternalAsync(eventHandlerActor);
      }

      // Create repository for this actor
      const repository = new DaprRepository(
        eventHandlerActor,
        this.partitionInfo.partitionKeys,
        this.partitionInfo.projector,
        this.domainTypes,
        this.currentAggregate
      );

      // Execute command with new ICommandWithHandler pattern
      const command = commandAndMetadata.command as ICommandWithHandler<TCommand, TProjector, TPayloadUnion, TAggregatePayload>;
      const commandData = commandAndMetadata.commandData;
      const metadata = commandAndMetadata.metadata || {
        timestamp: new Date(),
        requestId: crypto.randomUUID()
      };
      
      // Validate command
      const validateResult = command.validate(commandData);
      if (validateResult.isErr()) {
        return JSON.stringify({
          aggregateId: this.partitionInfo.partitionKeys.aggregateId,
          group: this.partitionInfo.partitionKeys.group || 'default',
          rootPartitionKey: this.partitionInfo.partitionKeys.rootPartitionKey || 'default',
          version: this.currentAggregate.version,
          events: [],
          error: validateResult.error
        });
      }

      // Create command context
      const context: ICommandContext = {
        repository,
        metadata
      };

      // Handle command with context
      const handleResult = await command.handle(context, commandData, this.currentAggregate as Aggregate<TAggregatePayload>);
      if (handleResult.isErr()) {
        return JSON.stringify({
          aggregateId: this.partitionInfo.partitionKeys.aggregateId,
          group: this.partitionInfo.partitionKeys.group || 'default',
          rootPartitionKey: this.partitionInfo.partitionKeys.rootPartitionKey || 'default',
          version: this.currentAggregate.version,
          events: [],
          error: handleResult.error
        });
      }

      const eventPayloads = handleResult.value;
      
      // Convert to events
      const events: IEvent[] = eventPayloads.map((payload, index) => ({
        id: SortableUniqueId.generate(),
        partitionKeys: this.partitionInfo!.partitionKeys,
        aggregateType: this.partitionInfo!.projector.aggregateTypeName,
        eventType: payload.constructor.name,
        version: this.currentAggregate.version + index + 1,
        payload: payload,
        metadata: {
          timestamp: new Date(),
          requestId: metadata.requestId
        }
      }));

      if (events.length > 0) {
        // Save events
        const lastSortableId = this.currentAggregate.lastSortableUniqueId?.toString() || '';
        const saveResult = await repository.save(lastSortableId, events);
        
        if (saveResult.isErr()) {
          throw saveResult.error;
        }

        // Update current aggregate with new events
        this.currentAggregate = repository.getProjectedAggregate(events).unwrapOr(this.currentAggregate);
        
        // Mark as changed
        this.hasUnsavedChanges = true;
      }

      return JSON.stringify({
        aggregateId: this.partitionInfo.partitionKeys.aggregateId,
        group: this.partitionInfo.partitionKeys.group || 'default',
        rootPartitionKey: this.partitionInfo.partitionKeys.rootPartitionKey || 'default',
        version: this.currentAggregate.version,
        events: events.map(e => ({
          eventType: e.eventType,
          payload: e.payload
        }))
      });
    } catch (error) {
      console.error('Failed to execute command:', error);
      return JSON.stringify({
        aggregateId: this.partitionInfo?.partitionKeys.aggregateId || '',
        group: this.partitionInfo?.partitionKeys.group || 'default',
        rootPartitionKey: this.partitionInfo?.partitionKeys.rootPartitionKey || 'default',
        version: 0,
        events: []
      });
    }
  }

  async saveStateCallbackAsync(state?: any): Promise<void> {
    if (this.hasUnsavedChanges) {
      await this.saveStateAsync();
    }
  }

  async receiveReminderAsync(
    reminderName: string,
    state: Buffer,
    dueTime: string,
    period: string
  ): Promise<void> {
    console.log(`Received reminder: ${reminderName}`);
  }

  private async ensureInitializedAsync(): Promise<void> {
    if (this.partitionInfo) {
      return; // Already initialized
    }

    try {
      console.log(`Initializing AggregateActor ${this.id.id} on first use`);
      
      // Initialize partition info only
      this.partitionInfo = await this.getPartitionInfoAsync();
      
      console.log(`AggregateActor ${this.id.id} initialization completed`);
    } catch (error) {
      console.error('Error during actor initialization:', error);
      throw error;
    }
  }

  private async getPartitionInfoAsync(): Promise<PartitionKeysAndProjector<any>> {
    // Try to get saved partition info
    const stateManager = this.getStateManager();
    const savedInfo = await stateManager.tryGetState<SerializedPartitionInfo>(
      AggregateActor.PARTITION_INFO_KEY
    );
    
    if (savedInfo.hasValue && savedInfo.value?.grainKey) {
      // Parse from saved grain key
      return this.parseGrainKey(savedInfo.value.grainKey);
    }

    // Parse from actor ID
    const actorId = this.id;
    const grainKey = actorId.includes(':') ? actorId.substring(actorId.indexOf(':') + 1) : actorId;
    
    const partitionInfo = this.parseGrainKey(grainKey);

    // Save for future use
    await stateManager.setState(
      AggregateActor.PARTITION_INFO_KEY, 
      { grainKey } as SerializedPartitionInfo
    );

    return partitionInfo;
  }

  private parseGrainKey(grainKey: string): PartitionKeysAndProjector<any> {
    // Format: default@WeatherForecast@123=WeatherForecastProjector
    const [partitionPart, projectorName] = grainKey.split('=');
    if (!partitionPart || !projectorName) {
      throw new Error(`Invalid grain key format: ${grainKey}`);
    }

    const partitionKeys = PartitionKeys.fromPrimaryKeysString(partitionPart);
    
    const ProjectorClass = this.domainTypes.projectorRegistry.get(projectorName);
    if (!ProjectorClass) {
      throw new Error(`Projector not found: ${projectorName}`);
    }

    const projector = new ProjectorClass() as AggregateProjector<any>;
    return new PartitionKeysAndProjector(partitionKeys, projector);
  }

  private getEventHandlerActor(): IAggregateEventHandlerActor {
    if (!this.partitionInfo) {
      throw new Error('Partition info not initialized. Call ensureInitializedAsync() first.');
    }

    const eventHandlerKey = this.partitionInfo.toEventHandlerGrainKey();
    const eventHandlerActorId = { id: `eventhandler:${eventHandlerKey}` };

    return this.actorProxyFactory.createActorProxy<IAggregateEventHandlerActor>(
      eventHandlerActorId,
      'AggregateEventHandlerActor'
    );
  }

  private async getStateInternalAsync(): Promise<Aggregate> {
    await this.ensureInitializedAsync();
    
    const eventHandlerActor = this.getEventHandlerActor();
    return await this.loadStateInternalAsync(eventHandlerActor);
  }

  private async loadStateInternalAsync(eventHandlerActor: IAggregateEventHandlerActor): Promise<Aggregate> {
    if (!this.partitionInfo) {
      throw new Error('Partition info not initialized');
    }

    const stateManager = this.getStateManager();
    const savedState = await stateManager.tryGetState<any>(AggregateActor.STATE_KEY);

    if (savedState.hasValue) {
      const aggregate = await this.serializationService.deserializeAggregateAsync(savedState.value);
      if (aggregate) {
        if (this.partitionInfo.projector.getVersion() !== aggregate.projectorVersion) {
          // Version mismatch - rebuild from events
          const emptyAggregate = Aggregate.emptyFromPartitionKeys(this.partitionInfo.partitionKeys);
          const repository = new DaprRepository(
            eventHandlerActor,
            this.partitionInfo.partitionKeys,
            this.partitionInfo.projector,
            this.domainTypes,
            emptyAggregate
          );

          const rebuiltAggregate = await repository.load();
          if (rebuiltAggregate.isErr()) {
            throw rebuiltAggregate.error;
          }
          this.currentAggregate = rebuiltAggregate.value;
          this.hasUnsavedChanges = true;
          return rebuiltAggregate.value;
        }

        // Load delta events
        const deltaEventDocuments = await eventHandlerActor.getDeltaEventsAsync(
          aggregate.lastSortableUniqueId?.toString() || '',
          -1
        );

        if (deltaEventDocuments.length > 0) {
          // Convert to events and project
          const deltaEvents: IEvent[] = deltaEventDocuments.map(doc => ({
            id: SortableUniqueId.fromString(doc.sortableUniqueId).unwrapOr(SortableUniqueId.generate()),
            partitionKeys: doc.partitionKeys,
            aggregateType: doc.partitionKeys.group || '',
            eventType: doc.eventType,
            version: doc.version,
            payload: doc.payload,
            metadata: {
              ...doc.metadata,
              timestamp: new Date(doc.createdAt)
            }
          }));

          // Project delta events
          let currentAggregate = aggregate;
          for (const event of deltaEvents) {
            const projectResult = this.partitionInfo.projector.project(currentAggregate, event);
            if (projectResult.isErr()) {
              throw projectResult.error;
            }
            currentAggregate = projectResult.value;
          }

          this.currentAggregate = currentAggregate;
          this.hasUnsavedChanges = true;
        } else {
          this.currentAggregate = aggregate;
        }

        return this.currentAggregate;
      }
    }

    // No saved state - load all events
    const emptyAggregate = Aggregate.emptyFromPartitionKeys(this.partitionInfo.partitionKeys);
    const repository = new DaprRepository(
      eventHandlerActor,
      this.partitionInfo.partitionKeys,
      this.partitionInfo.projector,
      this.domainTypes,
      emptyAggregate
    );

    const newAggregate = await repository.load();
    if (newAggregate.isErr()) {
      throw newAggregate.error;
    }
    this.currentAggregate = newAggregate.value;
    this.hasUnsavedChanges = true;
    return newAggregate.value;
  }

  private async rebuildStateInternalAsync(): Promise<Aggregate> {
    await this.ensureInitializedAsync();

    if (!this.partitionInfo) {
      throw new Error('Partition info not initialized');
    }

    const eventHandlerActor = this.getEventHandlerActor();

    // Create repository for rebuilding with empty aggregate
    const emptyAggregate = Aggregate.emptyFromPartitionKeys(this.partitionInfo.partitionKeys);
    const repository = new DaprRepository(
      eventHandlerActor,
      this.partitionInfo.partitionKeys,
      this.partitionInfo.projector,
      this.domainTypes,
      emptyAggregate
    );

    // Load all events and rebuild state
    const aggregate = await repository.load();
    if (aggregate.isErr()) {
      throw aggregate.error;
    }
    this.currentAggregate = aggregate.value;
    this.hasUnsavedChanges = true;

    // Save the rebuilt state
    await this.saveStateAsync();

    return aggregate.value;
  }

  private async saveStateAsync(): Promise<void> {
    const surrogate = await this.serializationService.serializeAggregateAsync(this.currentAggregate);
    const stateManager = this.getStateManager();
    await stateManager.setState(AggregateActor.STATE_KEY, surrogate);
    this.hasUnsavedChanges = false;
  }
}