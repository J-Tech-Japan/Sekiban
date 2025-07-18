import type { 
  SekibanDomainTypes,
  ICommandWithHandler,
  IAggregateProjector,
  ITypedAggregatePayload,
  EmptyAggregatePayload,
  Aggregate,
  Metadata,
  SekibanError,
  IEvent
} from '@sekiban/core';
import { PartitionKeys } from '@sekiban/core';
import { SortableUniqueId, globalRegistry } from '@sekiban/core';
import { ActorId } from '@dapr/dapr';
import { ok, err } from 'neverthrow';
import type { 
  SerializableCommandAndMetadata,
  SekibanCommandResponse
} from '../executor/interfaces.js';
import type { IActorProxyFactory } from '../types/index.js';
import { PartitionKeysAndProjector } from '../parts/index.js';
import type { IAggregateEventHandlerActor, SerializableEventDocument } from './interfaces.js';
/**
 * Domain implementation of AggregateActor with proper constructor injection
 * This class contains all the business logic and is testable
 */
export class AggregateActorImpl {
  private currentPartitionKeysAndProjector: PartitionKeysAndProjector<any> | null = null;
  private hasUnsavedChanges: boolean = false;

  constructor(
    private readonly actorId: string,
    private readonly domainTypes: SekibanDomainTypes,
    private readonly serviceProvider: any,
    private readonly actorProxyFactory: IActorProxyFactory,
    private readonly eventStore?: any,
    private readonly eventHandlerDirectCall?: (actorId: string, method: string, args: any[]) => Promise<any>
  ) {
    console.log(`[AggregateActorImpl] Created for actor ${actorId}`);
    console.log(`[AggregateActorImpl] Available command types:`, 
      this.domainTypes.commandTypes.getCommandTypes().map((c: any) => c.name)
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
          availableCommands: this.domainTypes.commandTypes.getCommandTypes().map((c: any) => c.name)
        } as any;
      }

      // Get the projector for this aggregate type
      const projectorTypeName = (metadata as any)?.projectorTypeName || partitionKeys.group + 'Projector';
      const projector = this.domainTypes.projectorTypes.getProjectorByAggregateType(partitionKeys.group || 'Unknown');
      
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
        console.error('[AggregateActorImpl] Available commands in registry:', globalRegistry.getCommandTypes().map((c: any) => c));
        console.error('[AggregateActorImpl] Available commands in domain types:', this.domainTypes.commandTypes.getCommandTypes().map((c: any) => c.name));
        return {
          success: false,
          error: `Command not found in registry: ${commandType}`
        } as any;
      }
      
      console.log('[AggregateActorImpl] Found command definition from registry:', commandDef);
      console.log('[AggregateActorImpl] Command def type:', typeof commandDef);
      console.log('[AggregateActorImpl] Command def keys:', commandDef ? Object.keys(commandDef) : 'null');
      console.log('[AggregateActorImpl] Command def handlers:', commandDef?.handlers ? Object.keys(commandDef.handlers) : 'no handlers');
      console.log('[AggregateActorImpl] Has handle in handlers?', commandDef?.handlers?.handle ? 'yes' : 'no');
      
      // For schema-based commands, we need to execute the handle function
      const events: any[] = [];
      
      try {
        // Create context for the handler that implements ICommandContext interface
        const context = {
          aggregate: currentState,
          aggregateId: partitionKeys.aggregateId,
          originalSortableUniqueId: currentState?.lastSortableUniqueId || '',
          events: [],
          partitionKeys: partitionKeys,
          metadata: metadata,
          
          // Methods required by ICommandContext
          getPartitionKeys: () => partitionKeys,
          getNextVersion: () => (currentState?.version || 0) + 1,
          getCurrentVersion: () => currentState?.version || 0,
          getAggregate: () => {
            return ok(currentState || { version: 0, events: [], lastSortableUniqueId: '' });
          },
          appendEvent: (eventPayload: any) => {
            console.log('[AggregateActorImpl] !!!! appendEvent called !!!!');
            console.log('[AggregateActorImpl] appendEvent payload:', eventPayload);
            console.log('[AggregateActorImpl] appendEvent payload type:', typeof eventPayload);
            console.log('[AggregateActorImpl] appendEvent payload constructor:', eventPayload?.constructor?.name);
            events.push(eventPayload);
            console.log('[AggregateActorImpl] Total events collected after push:', events.length);
            console.log('[AggregateActorImpl] Events array:', events);
            return ok({
              id: crypto.randomUUID(),
              eventType: eventPayload.constructor.name,
              payload: eventPayload,
              version: (currentState?.version || 0) + events.length,
              sortableUniqueId: SortableUniqueId.generate().toString(),
              createdAt: new Date()
            });
          },
          getService: () => {
            return err({ code: 'SERVICE_NOT_AVAILABLE', message: 'Service resolution not available in actor context' } as SekibanError);
          }
        };
        
        // Create a command instance with the command data
        if (!commandDef.create || typeof commandDef.create !== 'function') {
          console.error('[AggregateActorImpl] Command definition does not have create function');
          return {
            success: false,
            error: 'Command definition does not have create function'
          } as any;
        }
        
        console.log('[AggregateActorImpl] Creating command instance with data...');
        let commandInstance: any;
        try {
          commandInstance = commandDef.create(commandData);
        } catch (error) {
          console.error('[AggregateActorImpl] Failed to create command instance:', error);
          return {
            success: false,
            error: `Failed to create command: ${error instanceof Error ? error.message : 'Unknown error'}`
          } as any;
        }
        
        console.log('[AggregateActorImpl] Command instance created:', typeof commandInstance);
        console.log('[AggregateActorImpl] Command instance has handle?', typeof commandInstance.handle === 'function');
        console.log('[AggregateActorImpl] Command instance details:', {
          type: typeof commandInstance,
          constructor: commandInstance?.constructor?.name,
          keys: commandInstance ? Object.keys(commandInstance) : 'null',
          isSchemaCommand: commandInstance && 'data' in commandInstance && 'handle' in commandInstance
        });
        
        // Check if we need to get the handle function from the command definition
        let handleFunction = commandInstance.handle;
        
        // For schema-based commands, the handle function is in handlers.handle
        if (!handleFunction && commandDef.handlers?.handle) {
          console.log('[AggregateActorImpl] Using handle function from command definition handlers');
          handleFunction = commandDef.handlers.handle;
        } else if (!handleFunction && commandDef.handle) {
          console.log('[AggregateActorImpl] Using handle function from command definition');
          handleFunction = commandDef.handle;
        }
        
        // Call the handle method on the command instance
        if (typeof handleFunction === 'function') {
          console.log('[AggregateActorImpl] Calling command handle method...');
          console.log('[AggregateActorImpl] Passing context:', typeof context, context ? 'defined' : 'undefined');
          console.log('[AggregateActorImpl] Context has appendEvent?', context && typeof context.appendEvent === 'function' ? 'yes' : 'no');
          console.log('[AggregateActorImpl] Context.appendEvent details:', context && typeof context.appendEvent === 'function' ? 'function exists' : 'missing');
          
          let result;
          try {
            // If we have a command instance with handle method, call it directly
            if (commandInstance && typeof commandInstance.handle === 'function') {
              console.log('[AggregateActorImpl] Calling handle on command instance');
              // SchemaCommand.handle expects (command, context) but ignores the first parameter
              result = commandInstance.handle(commandData, context);
            } else if (typeof handleFunction === 'function') {
              console.log('[AggregateActorImpl] Calling handle function directly');
              // Call the handle function directly with data and context
              result = handleFunction(commandData, context);
            } else {
              throw new Error('No handle method available');
            }
          } catch (handleError) {
            console.error('[AggregateActorImpl] Command handle threw error:', handleError);
            throw handleError;
          }
          
          // Handle sync or async result
          const handleResult = result instanceof Promise ? await result : result;
          
          console.log('[AggregateActorImpl] Handle result:', handleResult);
          console.log('[AggregateActorImpl] Handle result type:', typeof handleResult);
          console.log('[AggregateActorImpl] Is Result type?', handleResult && typeof handleResult === 'object' && 'isOk' in handleResult);
          
          // Check if it's a Result type (neverthrow)
          if (handleResult && typeof handleResult === 'object' && 'isOk' in handleResult) {
            if (!handleResult.isOk()) {
              console.error('[AggregateActorImpl] Command handler returned error:', handleResult.error);
              return {
                success: false,
                error: handleResult.error.message || 'Command handler failed'
              } as any;
            }
            // If OK, the result value should be the events array
            const handlerEvents = handleResult.value;
            console.log('[AggregateActorImpl] Handler returned events:', handlerEvents);
            console.log('[AggregateActorImpl] Handler events type:', typeof handlerEvents);
            console.log('[AggregateActorImpl] Is array?', Array.isArray(handlerEvents));
            
            if (Array.isArray(handlerEvents)) {
              console.log('[AggregateActorImpl] Adding', handlerEvents.length, 'events from handler');
              events.push(...handlerEvents);
            }
          }
        } else {
          console.error('[AggregateActorImpl] Command handle function not found');
          console.error('[AggregateActorImpl] Command instance:', commandInstance);
          console.error('[AggregateActorImpl] Command definition:', commandDef);
          return {
            success: false,
            error: 'Command handle function not found'
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
      console.log('[AggregateActorImpl] Events array content:', JSON.stringify(events, null, 2));
      
      if (events.length === 0) {
        console.log('[AggregateActorImpl] WARNING: No events generated, returning early without calling EventHandlerActor');
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
        // Get event type from the payload's type property (created by defineEvent)
        const eventType = eventPayload.type || eventPayload.constructor.name;
        const eventDoc: SerializableEventDocument = {
          id: crypto.randomUUID(),
          sortableUniqueId: sortableUniqueId.toString(),
          eventType: eventType,
          aggregateId: partitionKeys.aggregateId,
          partitionKeys: partitionKeys,
          version: version++,
          payload: eventPayload,
          createdAt: new Date().toISOString(),
          metadata: {
            commandId: (metadata as any)?.commandId,
            causationId: (metadata as any)?.causationId,
            correlationId: (metadata as any)?.correlationId,
            executedUser: (metadata as any)?.executedUser
          }
        };
        eventDocuments.push(eventDoc);
      }
      
      console.log('[AggregateActorImpl] Created', eventDocuments.length, 'event documents');
      console.log('[AggregateActorImpl] Event documents:', JSON.stringify(eventDocuments, null, 2));
      
      // Call AggregateEventHandlerActor in the separate service
      const eventHandlerActorId = `${partitionKeys.group}:${partitionKeys.aggregateId}:${partitionKeys.rootPartitionKey || 'default'}`;
      console.log('[AggregateActorImpl] Getting event handler actor:', eventHandlerActorId);
      
      // Create proxy for AggregateEventHandlerActor (in separate service)
      const eventHandlerActor = this.actorProxyFactory.createActorProxy(
        new ActorId(eventHandlerActorId),
        'AggregateEventHandlerActor'
      ) as any;
      
      console.log('[AggregateActorImpl] Calling appendEventsAsync on AggregateEventHandlerActor');
      const appendResult = await eventHandlerActor.appendEventsAsync(
        currentState?.lastSortableUniqueId || '',
        eventDocuments
      );
      
      if (!appendResult || !appendResult.isSuccess) {
        console.error('[AggregateActorImpl] Failed to append events:', appendResult?.error);
        return {
          success: false,
          error: appendResult?.error || 'Failed to append events'
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
      console.log('[AggregateActorImpl] Getting event handler actor:', eventHandlerActorId);
      
      // Create proxy for AggregateEventHandlerActor (in separate service)
      const eventHandlerActor = this.actorProxyFactory.createActorProxy(
        new ActorId(eventHandlerActorId),
        'AggregateEventHandlerActor'
      ) as any;
      
      console.log('[AggregateActorImpl] Calling getAllEventsAsync on AggregateEventHandlerActor');
      const events = await eventHandlerActor.getAllEventsAsync();
      console.log('[AggregateActorImpl] Loaded', (events as any[]).length, 'events from event handler');
      
      if (!events || events.length === 0) {
        return null;
      }
      
      // Apply events using projector
      let aggregate: any = null;
      let lastSortableUniqueId = '';
      
      for (const eventDoc of (events as any[])) {
        // Create event instance from payload
        const event: IEvent<any> = {
          id: eventDoc.id,
          sortableUniqueId: SortableUniqueId.fromString(eventDoc.sortableUniqueId),
          partitionKeys: eventDoc.partitionKeys,
          aggregateType: partitionKeys.group || 'Unknown',
          eventType: eventDoc.eventType,
          aggregateId: eventDoc.aggregateId || partitionKeys.aggregateId,
          version: eventDoc.version,
          payload: eventDoc.payload,
          timestamp: new Date(eventDoc.createdAt),
          metadata: {
            timestamp: new Date(eventDoc.createdAt),
            correlationId: eventDoc.metadata?.correlationId,
            causationId: eventDoc.metadata?.causationId,
            executedUser: eventDoc.metadata?.executedUser || 'system'
          },
          // C# compatibility properties
          partitionKey: eventDoc.partitionKeys.partitionKey,
          aggregateGroup: eventDoc.partitionKeys.group || 'default'
        };
        
        // Apply event to aggregate
        const applyResult = projector.project(aggregate, event);
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
    
    // Extract partition keys from actor ID if not already set
    let partitionKeys: PartitionKeys;
    let projector: IAggregateProjector<any>;
    
    if (this.currentPartitionKeysAndProjector) {
      partitionKeys = this.currentPartitionKeysAndProjector.partitionKeys;
      projector = this.domainTypes.projectorTypes.getProjectorByAggregateType(partitionKeys.group || 'Unknown');
    } else {
      // Parse actor ID: "rootPartition@group@aggregateId=projectorType"
      const parts = this.actorId.split('@');
      const lastPart = parts[parts.length - 1];
      const [aggregateId, projectorType] = lastPart.split('=');
      const group = parts[1];
      const rootPartition = parts[0];
      
      partitionKeys = new PartitionKeys(aggregateId, group, rootPartition);
      projector = this.domainTypes.projectorTypes.getProjectorByAggregateType(group);
      
      console.log(`[AggregateActorImpl] Extracted from actor ID - group: ${group}, aggregateId: ${aggregateId}, rootPartition: ${rootPartition}`);
    }
    
    if (!projector) {
      console.log(`[AggregateActorImpl] No projector found for group: ${partitionKeys.group}`);
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

  /**
   * Call event handler actor directly via HTTP to avoid ActorProxyBuilder issues
   */
  private async callEventHandlerDirectly(
    actorId: string, 
    methodName: string, 
    args: any[]
  ): Promise<any> {
    try {
      // For same-app communication, we can use HTTP directly
      const response = await fetch(`http://127.0.0.1:${process.env.PORT || 3000}/actors/AggregateEventHandlerActor/${actorId}/method/${methodName}`, {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
          'dapr-app-id': process.env.DAPR_APP_ID || 'sekiban-app'
        },
        body: JSON.stringify(args)
      });
      
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}: ${response.statusText}`);
      }
      
      return await response.json();
    } catch (error) {
      console.error(`[AggregateActorImpl] Direct HTTP call failed for ${methodName}:`, error);
      throw error;
    }
  }
}