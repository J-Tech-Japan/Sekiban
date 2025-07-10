import type { 
  SekibanDomainTypes,
  ICommandWithHandler,
  IAggregateProjector,
  ITypedAggregatePayload,
  EmptyAggregatePayload,
  PartitionKeys,
  Aggregate,
  Metadata,
  SekibanError,
  IEvent,
  IEventPayload
} from '@sekiban/core';
import { SortableUniqueId, globalRegistry } from '@sekiban/core';
import { ok, err } from 'neverthrow';
import type { 
  SerializableCommandAndMetadata,
  SekibanCommandResponse,
  IDaprSerializationService,
  IActorProxyFactory
} from '../executor/interfaces.js';
import { PartitionKeysAndProjector } from '../parts/index.js';
import type { IAggregateEventHandlerActor, SerializableEventDocument } from './interfaces.js';

/**
 * Domain implementation of AggregateActor with proper constructor injection
 * This class contains all the business logic and is testable
 */
export class AggregateActorImpl {
  private currentPartitionKeysAndProjector: PartitionKeysAndProjector | null = null;
  private hasUnsavedChanges: boolean = false;

  constructor(
    private readonly actorId: string,
    private readonly domainTypes: SekibanDomainTypes,
    private readonly serviceProvider: any,
    private readonly actorProxyFactory: IActorProxyFactory,
    private readonly serializationService: IDaprSerializationService,
    private readonly eventStore?: any
  ) {
    console.log(`[AggregateActorImpl] Created for actor ${actorId}`);
    console.log(`[AggregateActorImpl] Available command types:`, 
      this.domainTypes.commandTypes.getCommandTypes().map(c => c.name)
    );
  }

  /**
   * Initialize the actor (called from onActivate)
   */
  async initialize(): Promise<void> {
    console.log(`[AggregateActorImpl] Initializing actor ${this.actorId}`);
    // Any async initialization logic here
  }

  /**
   * Cleanup (called from onDeactivate)
   */
  async cleanup(): Promise<void> {
    console.log(`[AggregateActorImpl] Cleaning up actor ${this.actorId}`);
    if (this.hasUnsavedChanges) {
      await this.saveStateAsync();
    }
  }

  /**
   * Execute a command
   */
  async executeCommandAsync<
    TCommand,
    TProjector extends IAggregateProjector<TPayloadUnion>,
    TPayloadUnion extends ITypedAggregatePayload,
    TAggregatePayload extends TPayloadUnion | EmptyAggregatePayload = TPayloadUnion | EmptyAggregatePayload
  >(
    commandAndMetadata: SerializableCommandAndMetadata<TCommand, TProjector, TPayloadUnion, TAggregatePayload>
  ): Promise<SekibanCommandResponse> {
    console.log(`[AggregateActorImpl] executeCommandAsync called`);
    console.log(`[AggregateActorImpl] Raw input:`, JSON.stringify(commandAndMetadata, null, 2));
    
    try {
      // Extract command information from the correct structure
      const commandType = commandAndMetadata.commandType;
      const commandData = commandAndMetadata.commandData;
      const partitionKeys = commandAndMetadata.partitionKeys;
      const metadata = commandAndMetadata.metadata;
      
      console.log('[AggregateActorImpl] Command type:', commandType);
      console.log('[AggregateActorImpl] Command data:', commandData);
      console.log('[AggregateActorImpl] Partition keys:', partitionKeys);
      console.log('[AggregateActorImpl] Metadata:', metadata);
      
      // Validate command exists
      const commandTypeDef = this.domainTypes.commandTypes.getCommandTypeByName(commandType);
      if (!commandTypeDef) {
        return {
          success: false,
          error: `Unknown command type: ${commandType}`,
          availableCommands: this.domainTypes.commandTypes.getCommandTypes().map(c => c.name)
        } as any;
      }

      // Get the projector for this aggregate type
      const projectorTypeName = metadata.projectorTypeName || partitionKeys.group + 'Projector';
      const projector = this.domainTypes.projectorTypes.getProjectorByAggregateType(partitionKeys.group);
      
      if (!projector) {
        return {
          success: false,
          error: `No projector found for aggregate type: ${partitionKeys.group}`
        } as any;
      }
      
      console.log('[AggregateActorImpl] Found projector:', projectorTypeName);
      
      // Load current aggregate state
      console.log('[AggregateActorImpl] Loading current aggregate state...');
      const currentState = await this.loadAggregateStateAsync(partitionKeys, projector);
      console.log('[AggregateActorImpl] Current aggregate version:', currentState?.version || 0);
      
      // Execute the command handler
      console.log('[AggregateActorImpl] Executing command handler...');
      
      // Get the actual command definition from the global registry
      const commandDef = globalRegistry.getCommand(commandType);
      
      if (!commandDef) {
        console.error('[AggregateActorImpl] Command not found in global registry');
        console.error('[AggregateActorImpl] Available commands in registry:', globalRegistry.getCommands().map(c => c.commandType));
        console.error('[AggregateActorImpl] Available commands in domain types:', this.domainTypes.commandTypes.getCommandTypes().map(c => c.name));
        return {
          success: false,
          error: `Command not found in registry: ${commandType}`
        } as any;
      }
      
      console.log('[AggregateActorImpl] Found command definition from registry:', commandDef);
      console.log('[AggregateActorImpl] Command def type:', typeof commandDef);
      console.log('[AggregateActorImpl] Command def keys:', commandDef ? Object.keys(commandDef) : 'null');
      
      // For schema-based commands, we need to execute the handle function
      const events: IEventPayload[] = [];
      
      try {
        // Create context for the handler
        const context = {
          aggregate: currentState,
          aggregateId: partitionKeys.aggregateId,
          appendEvent: (event: IEventPayload) => {
            events.push(event);
          }
        };
        
        // Execute the handle function - schema-based commands have handle directly on the command
        if (typeof commandDef.handle === 'function') {
          const result = commandDef.handle(commandData, context);
          
          // Handle sync or async result
          const handleResult = result instanceof Promise ? await result : result;
          
          // Check if it's a Result type (neverthrow)
          if (handleResult && typeof handleResult === 'object' && 'isOk' in handleResult) {
            if (!handleResult.isOk()) {
              console.error('[AggregateActorImpl] Command handler returned error:', handleResult.error);
              return {
                success: false,
                error: handleResult.error.message || 'Command handler failed'
              } as any;
            }
            // If OK, the events are already appended via context.appendEvent
          }
        } else {
          console.error('[AggregateActorImpl] Command does not have handle function');
          console.error('[AggregateActorImpl] Command def structure:', JSON.stringify(Object.keys(commandDef)));
          console.error('[AggregateActorImpl] Command def type:', typeof commandDef);
          return {
            success: false,
            error: 'Command does not have handle function'
          } as any;
        }
      } catch (error) {
        console.error('[AggregateActorImpl] Command handler failed:', error);
        return {
          success: false,
          error: error instanceof Error ? error.message : 'Command handler failed'
        } as any;
      }
      
      console.log('[AggregateActorImpl] Command generated', events.length, 'events');
      
      if (events.length === 0) {
        // No events generated, return success with current state
        return {
          aggregateId: this.actorId,
          lastSortableUniqueId: currentState?.lastSortableUniqueId || '',
          success: true,
          metadata: {
            version: currentState?.version || 0,
            commandType: commandType,
            processedAt: new Date().toISOString()
          }
        };
      }
      
      // Create event documents with proper metadata
      const eventDocuments: SerializableEventDocument[] = [];
      let version = (currentState?.version || 0) + 1;
      
      for (const eventPayload of events) {
        const sortableUniqueId = SortableUniqueId.generate();
        const eventDoc: SerializableEventDocument = {
          id: crypto.randomUUID(),
          sortableUniqueId: sortableUniqueId.toString(),
          eventType: eventPayload.constructor.name,
          aggregateId: partitionKeys.aggregateId,
          partitionKeys: partitionKeys,
          version: version++,
          payload: eventPayload,
          createdAt: new Date().toISOString(),
          metadata: {
            commandId: metadata.commandId,
            causationId: metadata.causationId,
            correlationId: metadata.correlationId,
            executedUser: metadata.executedUser
          }
        };
        eventDocuments.push(eventDoc);
      }
      
      console.log('[AggregateActorImpl] Created', eventDocuments.length, 'event documents');
      
      // Get the event handler actor
      const eventHandlerActorId = `${partitionKeys.group}:${partitionKeys.aggregateId}:${partitionKeys.rootPartitionKey || 'default'}`;
      console.log('[AggregateActorImpl] Getting event handler actor:', eventHandlerActorId);
      
      // Use ActorProxyBuilder for actor-to-actor communication
      console.log('[AggregateActorImpl] Creating event handler actor proxy via ActorProxyBuilder...');
      const eventHandlerProxy = this.actorProxyFactory.createActorProxy(
        { id: eventHandlerActorId },
        'AggregateEventHandlerActor'
      ) as IAggregateEventHandlerActor;
      
      // Append events to the event handler
      console.log('[AggregateActorImpl] Appending events to event handler via ActorProxyBuilder...');
      const appendResult = await eventHandlerProxy.appendEventsAsync(
        currentState?.lastSortableUniqueId || '',
        eventDocuments
      );
      
      if (!appendResult.isSuccess) {
        console.error('[AggregateActorImpl] Failed to append events:', appendResult.error);
        return {
          success: false,
          error: appendResult.error || 'Failed to append events'
        } as any;
      }
      
      console.log('[AggregateActorImpl] Events appended successfully');
      
      // Update our state
      this.hasUnsavedChanges = true;
      
      // Return success response
      const response: SekibanCommandResponse = {
        aggregateId: this.actorId,
        lastSortableUniqueId: appendResult.lastSortableUniqueId!,
        success: true,
        metadata: {
          version: version - 1,
          commandType: commandType,
          processedAt: new Date().toISOString(),
          eventCount: events.length
        }
      };
      
      console.log('[AggregateActorImpl] Returning response:', response);
      return response;
      
    } catch (error) {
      console.error('[AggregateActorImpl] Error in executeCommandAsync:', error);
      return {
        success: false,
        error: error instanceof Error ? error.message : 'Unknown error'
      } as any;
    }
  }

  /**
   * Load aggregate state from event handler
   */
  private async loadAggregateStateAsync(
    partitionKeys: PartitionKeys,
    projector: IAggregateProjector<any>
  ): Promise<any> {
    try {
      // Get the event handler actor
      const eventHandlerActorId = `${partitionKeys.group}:${partitionKeys.aggregateId}:${partitionKeys.rootPartitionKey || 'default'}`;
      
      // Use ActorProxyBuilder for actor-to-actor communication
      console.log('[AggregateActorImpl] Creating event handler actor proxy via ActorProxyBuilder...');
      const eventHandlerProxy = this.actorProxyFactory.createActorProxy(
        { id: eventHandlerActorId },
        'AggregateEventHandlerActor'
      ) as IAggregateEventHandlerActor;
      
      console.log('[AggregateActorImpl] Calling event handler actor via ActorProxyBuilder...');
      const events = await eventHandlerProxy.getAllEventsAsync();
      console.log('[AggregateActorImpl] Loaded', events.length, 'events from event handler');
      
      if (events.length === 0) {
        return null;
      }
      
      // Apply events using projector
      let aggregate: any = null;
      let lastSortableUniqueId = '';
      
      for (const eventDoc of events) {
        // Create event instance from payload
        const event: IEvent<IEventPayload> = {
          id: eventDoc.id,
          sortableUniqueId: SortableUniqueId.fromString(eventDoc.sortableUniqueId),
          partitionKeys: eventDoc.partitionKeys,
          aggregateType: partitionKeys.group,
          eventType: eventDoc.eventType,
          aggregateId: eventDoc.aggregateId,
          version: eventDoc.version,
          payload: eventDoc.payload,
          timestamp: new Date(eventDoc.createdAt),
          correlationId: eventDoc.metadata?.correlationId,
          causationId: eventDoc.metadata?.causationId,
          createdUser: eventDoc.metadata?.executedUser || 'system'
        };
        
        // Apply event to aggregate
        const applyResult = projector.applyEvent(event, aggregate);
        if (applyResult.isOk()) {
          aggregate = applyResult.value;
          lastSortableUniqueId = eventDoc.sortableUniqueId;
        } else {
          console.error('[AggregateActorImpl] Failed to apply event:', applyResult.error);
        }
      }
      
      // Add metadata to aggregate
      if (aggregate) {
        aggregate.lastSortableUniqueId = lastSortableUniqueId;
      }
      
      return aggregate;
    } catch (error) {
      console.error('[AggregateActorImpl] Error loading aggregate state:', error);
      return null;
    }
  }

  /**
   * Get aggregate state
   */
  async getAggregateStateAsync<
    TPayload extends ITypedAggregatePayload = ITypedAggregatePayload
  >(): Promise<Aggregate<TPayload> | null> {
    console.log(`[AggregateActorImpl] getAggregateStateAsync called`);
    
    if (!this.currentPartitionKeysAndProjector) {
      return null;
    }

    const partitionKeys = this.currentPartitionKeysAndProjector.partitionKeys;
    const projector = this.domainTypes.projectorTypes.getProjectorByAggregateType(partitionKeys.group);
    
    if (!projector) {
      return null;
    }
    
    return this.loadAggregateStateAsync(partitionKeys, projector);
  }

  /**
   * Save state callback (for timer)
   */
  async saveStateCallbackAsync(): Promise<void> {
    if (this.hasUnsavedChanges) {
      await this.saveStateAsync();
    }
  }

  /**
   * Save state
   */
  async saveStateAsync(): Promise<void> {
    console.log(`[AggregateActorImpl] Saving state for actor ${this.actorId}`);
    this.hasUnsavedChanges = false;
    // Actual state saving logic would go here
  }

  /**
   * Rebuild state from events
   */
  async rebuildStateAsync(): Promise<void> {
    console.log(`[AggregateActorImpl] Rebuilding state for actor ${this.actorId}`);
    // State rebuilding logic would go here
  }

  /**
   * Get partition info
   */
  async getPartitionInfoAsync(): Promise<any> {
    return {
      actorId: this.actorId,
      partitionKeys: this.currentPartitionKeysAndProjector?.partitionKeys
    };
  }

  /**
   * Handle reminder
   */
  async receiveReminder(reminderName: string, state: any): Promise<void> {
    console.log(`[AggregateActorImpl] Received reminder ${reminderName}`);
    
    if (reminderName === 'SaveState') {
      await this.saveStateCallbackAsync();
    }
  }
}