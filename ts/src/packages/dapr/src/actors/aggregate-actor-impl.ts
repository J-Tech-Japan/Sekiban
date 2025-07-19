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
  private _currentAggregate: any = null;
  private _lastLoadedSortableUniqueId: string = '';

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
      const projectorLookup = this.domainTypes.projectorTypes.getProjectorByAggregateType(partitionKeys.group || 'Unknown');
      const projector = projectorLookup ? (typeof projectorLookup === 'function' ? new projectorLookup() : projectorLookup) : null;
      
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
      console.log('[AggregateActorImpl] Current aggregate payload:', JSON.stringify(currentState, null, 2));
      
      // Execute the command handler
      console.log('[AggregateActorImpl] Executing command handler...');
      console.log('[AggregateActorImpl] Current state before command:', currentState ? 'exists' : 'null');
      console.log('[AggregateActorImpl] Current state version:', currentState?.version || 0);
      
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
      const eventHandlerActorId = `${partitionKeys.group}-${partitionKeys.aggregateId}-${partitionKeys.rootPartitionKey || 'default'}`;
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
      
      console.log('[AggregateActorImpl] appendEventsAsync returned:', JSON.stringify(appendResult));
      
      if (!appendResult || !appendResult.isSuccess) {
        console.error('[AggregateActorImpl] Failed to append events:', appendResult?.error);
        return {
          success: false,
          error: appendResult?.error || 'Failed to append events'
        } as any;
      }
      
      console.log('[AggregateActorImpl] Events appended successfully');
      
      // Update cached state with new events
      if (this._currentAggregate && projector) {
        console.log('[AggregateActorImpl] Updating cached aggregate state with new events');
        // Apply the new events to the cached state
        for (const event of events) {
          const applyResult = projector.project(this._currentAggregate, event);
          if (applyResult.isOk()) {
            this._currentAggregate = applyResult.value;
          }
        }
        this._lastLoadedSortableUniqueId = appendResult.lastSortableUniqueId;
        
        // Update the lastSortableUniqueId in the aggregate state as well
        if (this._currentAggregate) {
          if (Object.isFrozen(this._currentAggregate) || Object.isSealed(this._currentAggregate)) {
            this._currentAggregate = { ...this._currentAggregate, lastSortableUniqueId: appendResult.lastSortableUniqueId };
          } else {
            this._currentAggregate.lastSortableUniqueId = appendResult.lastSortableUniqueId;
          }
        }
        
        console.log('[AggregateActorImpl] Updated cache, new last ID:', this._lastLoadedSortableUniqueId);
      } else if (!this._currentAggregate) {
        // If we don't have cached state yet, load it now with the new events
        console.log('[AggregateActorImpl] No cached state, loading aggregate after command execution');
        const updatedState = await this.loadAggregateStateAsync(partitionKeys, projector);
        // The loadAggregateStateAsync will cache it for us
      }
      
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
      console.log('[AggregateActorImpl] loadAggregateStateAsync called');
      console.log('[AggregateActorImpl] Current cache state:', {
        hasCachedAggregate: !!this._currentAggregate,
        cachedVersion: this._currentAggregate?.version,
        lastLoadedSortableUniqueId: this._lastLoadedSortableUniqueId
      });
      
      // For now, we'll always reload state to ensure consistency
      // TODO: Implement proper cache validation once EventHandlerActor has getLatestSnapshotAsync
      console.log('[AggregateActorImpl] Skipping cache, always reloading state for consistency');
      
      console.log('[AggregateActorImpl] No cached state, loading from storage or events');
      // Get the event handler actor
      const eventHandlerActorId = `${partitionKeys.group}-${partitionKeys.aggregateId}-${partitionKeys.rootPartitionKey || 'default'}`;
      console.log('[AggregateActorImpl] Getting event handler actor:', eventHandlerActorId);
      
      // Create proxy for AggregateEventHandlerActor (in separate service)
      const eventHandlerActor = this.actorProxyFactory.createActorProxy(
        new ActorId(eventHandlerActorId),
        'AggregateEventHandlerActor'
      ) as any;
      
      // Get all events (we'll optimize with delta loading later)
      console.log('[AggregateActorImpl] Calling getAllEventsAsync on AggregateEventHandlerActor');
      const events = await eventHandlerActor.getAllEventsAsync();
      console.log('[AggregateActorImpl] Loaded', (events as any[]).length, 'events from event handler');
      console.log('[AggregateActorImpl] Raw events from handler:', JSON.stringify(events, null, 2));
      
      if (!events || events.length === 0) {
        console.warn('[AggregateActorImpl] WARNING: No events returned from EventHandler, returning null aggregate');
        console.log('[AggregateActorImpl] No events found, returning null');
        this._currentAggregate = null;
        this._lastLoadedSortableUniqueId = '';
        return null;
      }
      
      // Apply events using projector
      let aggregate: any = null;
      let lastSortableUniqueId: string = '';
      
      // Initialize aggregate state using projector
      if (projector && projector.getInitialState) {
        const partitionKeysForInit = { aggregateId: partitionKeys.aggregateId, group: partitionKeys.group };
        aggregate = projector.getInitialState(partitionKeysForInit);
      } else {
        aggregate = {};
      }
      
      console.log('[AggregateActorImpl] Starting event projection with projector:', projector.aggregateTypeName);
      console.log('[AggregateActorImpl] Projector can handle events:', projector.getSupportedPayloadTypes ? projector.getSupportedPayloadTypes() : ['Task', 'CompletedTask']);
      
      for (const eventDoc of (events as any[])) {
        // Handle serialization differences like in multi-projector-actor
        const eventType = eventDoc.PayloadTypeName || eventDoc.eventType;
        const aggregateId = eventDoc.AggregateId || eventDoc.aggregateId || partitionKeys.aggregateId;
        const sortableIdStr = eventDoc.SortableUniqueId || eventDoc.sortableUniqueId || eventDoc.id;
        
        console.log('[AggregateActorImpl] Processing event:', eventType, 'for aggregate:', aggregateId);
        console.log('[AggregateActorImpl] Event doc fields:', {
          hasPayloadTypeName: !!eventDoc.PayloadTypeName,
          hasEventType: !!eventDoc.eventType,
          hasAggregateId: !!eventDoc.AggregateId,
          hasaggregateId: !!eventDoc.aggregateId,
          sortableIdFormat: sortableIdStr
        });
        
        // Handle SortableUniqueId conversion with error handling
        const sortableIdResult = SortableUniqueId.fromString(sortableIdStr);
        const sortableId = sortableIdResult.isOk() ? sortableIdResult.value : SortableUniqueId.create();
        
        // Handle payload decompression if needed (like in multi-projector-actor)
        let payload: any;
        try {
          if (eventDoc.CompressedPayloadJson) {
            const payloadBuffer = Buffer.from(eventDoc.CompressedPayloadJson, 'base64');
            // Check for gzip header (1f 8b)
            if (payloadBuffer[0] === 0x1f && payloadBuffer[1] === 0x8b) {
              // It's gzipped, decompress it
              const { gunzip } = await import('node:zlib');
              const { promisify } = await import('node:util');
              const ungzipAsync = promisify(gunzip);
              const decompressed = await ungzipAsync(payloadBuffer);
              const payloadJson = decompressed.toString('utf-8');
              payload = JSON.parse(payloadJson);
            } else {
              // Not gzipped, just base64 encoded JSON
              const payloadJson = payloadBuffer.toString('utf-8');
              payload = JSON.parse(payloadJson);
            }
          } else {
            payload = eventDoc.payload || {};
          }
        } catch (error) {
          console.error('[AggregateActorImpl] Error decompressing payload:', error);
          payload = eventDoc.payload || {};
        }
        
        // Reconstruct partition keys properly
        const reconstructedPartitionKeys = eventDoc.partitionKeys || {
          aggregateId: aggregateId,
          group: eventDoc.AggregateGroup || eventDoc.aggregateType || partitionKeys.group || 'Unknown',
          rootPartitionKey: eventDoc.RootPartitionKey || 'default',
          partitionKey: eventDoc.PartitionKey || partitionKeys.partitionKey || `${partitionKeys.group}-${aggregateId}`
        };
        
        // Create event instance from payload with proper field mapping
        const event: IEvent<any> = {
          id: sortableId,
          sortableUniqueId: sortableId,
          partitionKeys: reconstructedPartitionKeys,
          aggregateType: eventDoc.AggregateGroup || eventDoc.aggregateType || partitionKeys.group || 'Unknown',
          eventType: eventType,
          aggregateId: aggregateId,
          version: eventDoc.Version || eventDoc.version,
          payload: payload,
          timestamp: new Date(eventDoc.TimeStamp || eventDoc.createdAt),
          metadata: {
            timestamp: new Date(eventDoc.TimeStamp || eventDoc.createdAt),
            correlationId: eventDoc.CorrelationId || eventDoc.metadata?.correlationId,
            causationId: eventDoc.CausationId || eventDoc.metadata?.causationId,
            executedUser: eventDoc.ExecutedUser || eventDoc.metadata?.executedUser || 'system'
          },
          // C# compatibility properties
          partitionKey: eventDoc.PartitionKey || reconstructedPartitionKeys.partitionKey,
          aggregateGroup: eventDoc.AggregateGroup || eventDoc.aggregateType || reconstructedPartitionKeys.group || 'default'
        };
        
        console.log('[AggregateActorImpl] Created event object:', JSON.stringify(event, null, 2));
        console.log('[AggregateActorImpl] Current aggregate before projection:', aggregate);
        console.log('[AggregateActorImpl] Projector type:', projector.constructor.name);
        console.log('[AggregateActorImpl] Projector has project method?', typeof projector.project === 'function');
        
        // Apply event to aggregate
        try {
          const applyResult = projector.project(aggregate, event);
          console.log('[AggregateActorImpl] Projection result success:', applyResult.isOk());
          console.log('[AggregateActorImpl] Projection result:', applyResult);
        
        if (applyResult.isOk()) {
          aggregate = applyResult.value;
          lastSortableUniqueId = eventDoc.sortableUniqueId;
          console.log('[AggregateActorImpl] Projection successful, new aggregate:', JSON.stringify(aggregate, null, 2));
        } else {
          console.error('[AggregateActorImpl] Failed to apply event:', applyResult.error);
          console.error('[AggregateActorImpl] Event that failed:', JSON.stringify(event, null, 2));
        }
        } catch (projError) {
          console.error('[AggregateActorImpl] EXCEPTION during projection:', projError);
          console.error('[AggregateActorImpl] Error stack:', (projError as Error).stack);
          console.error('[AggregateActorImpl] Event causing error:', JSON.stringify(event, null, 2));
        }
      }
      
      // Add metadata to aggregate
      if (aggregate) {
        // Handle readonly/frozen aggregates by creating a new object
        if (Object.isFrozen(aggregate) || Object.isSealed(aggregate)) {
          aggregate = { ...aggregate, lastSortableUniqueId };
        } else {
          aggregate.lastSortableUniqueId = lastSortableUniqueId;
        }
      }
      
      // Cache the state for future use
      this._currentAggregate = aggregate;
      this._lastLoadedSortableUniqueId = lastSortableUniqueId;
      console.log('[AggregateActorImpl] Cached aggregate state, last ID:', lastSortableUniqueId);
      
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
      const projectorLookup2 = this.domainTypes.projectorTypes.getProjectorByAggregateType(partitionKeys.group || 'Unknown');
      projector = projectorLookup2 ? (typeof projectorLookup2 === 'function' ? new projectorLookup2() : projectorLookup2) : null;
    } else {
      // Parse actor ID: "rootPartition@group@aggregateId=projectorType"
      const parts = this.actorId.split('@');
      const lastPart = parts[parts.length - 1];
      const [aggregateId, projectorType] = lastPart.split('=');
      const group = parts[1];
      const rootPartition = parts[0];
      
      partitionKeys = new PartitionKeys(aggregateId, group, rootPartition);
      const projectorLookup3 = this.domainTypes.projectorTypes.getProjectorByAggregateType(group);
      projector = projectorLookup3 ? (typeof projectorLookup3 === 'function' ? new projectorLookup3() : projectorLookup3) : null;
      
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