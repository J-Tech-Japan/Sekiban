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
  }

  /**
   * Initialize the actor (called from onActivate)
   */
  async initialize(): Promise<void> {
    // Any async initialization logic here
  }

  /**
   * Cleanup (called from onDeactivate)
   */
  async cleanup(): Promise<void> {
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
    try {
      // Extract command information from the correct structure
      const commandType = commandAndMetadata.commandType;
      const commandData = commandAndMetadata.commandData;
      const partitionKeys = commandAndMetadata.partitionKeys;
      const metadata = commandAndMetadata.metadata;
      
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
      
      // Load current aggregate state
      const currentState = await this.loadAggregateStateAsync(partitionKeys, projector as unknown as IAggregateProjector<any>);
      
      // Get the actual command definition from domain types (global registry is empty in actor context)
      let commandDef: any;
      
      // Check if domainTypes.commandTypes has a registry property with commandDefinitions
      if ((this.domainTypes.commandTypes as any).registry && (this.domainTypes.commandTypes as any).registry.commandDefinitions) {
        commandDef = (this.domainTypes.commandTypes as any).registry.commandDefinitions.get(commandType);
      }
      
      if (!commandDef) {
        // Fallback to using getCommandTypes
        const commandTypesArray = this.domainTypes.commandTypes.getCommandTypes();
        
        // Find the command type info
        const commandTypeInfo = commandTypesArray.find((c: any) => c.name === commandType);
        
        if (!commandTypeInfo) {
          return {
            success: false,
            error: `Command not found in domain types: ${commandType}`
          } as any;
        }
        
        // The commandTypeInfo has { name, constructor }, we need to get the actual command definition
        // The constructor property contains the actual command class/definition
        commandDef = commandTypeInfo.constructor;
      }
      
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
            events.push(eventPayload);
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
          return {
            success: false,
            error: 'Command definition does not have create function'
          } as any;
        }
        
        let commandInstance: any;
        try {
          commandInstance = commandDef.create(commandData);
        } catch (error) {
          return {
            success: false,
            error: `Failed to create command: ${error instanceof Error ? error.message : 'Unknown error'}`
          } as any;
        }
        
        // Check if we need to get the handle function from the command definition
        let handleFunction = commandInstance.handle;
        
        // For schema-based commands, the handle function is in handlers.handle
        if (!handleFunction && commandDef.handlers?.handle) {
          handleFunction = commandDef.handlers.handle;
        } else if (!handleFunction && commandDef.handle) {
          handleFunction = commandDef.handle;
        }
        
        // Call the handle method on the command instance
        if (typeof handleFunction === 'function') {
          let result;
          try {
            // If we have a command instance with handle method, call it directly
            if (commandInstance && typeof commandInstance.handle === 'function') {
              // SchemaCommand.handle expects (command, context) but ignores the first parameter
              result = commandInstance.handle(commandData, context);
            } else if (typeof handleFunction === 'function') {
              // Call the handle function directly with data and context
              result = handleFunction(commandData, context);
            } else {
              throw new Error('No handle method available');
            }
          } catch (handleError) {
            throw handleError;
          }
          
          // Handle sync or async result
          const handleResult = result instanceof Promise ? await result : result;
          
          // Check if it's a Result type (neverthrow)
          if (handleResult && typeof handleResult === 'object' && 'isOk' in handleResult) {
            if (!handleResult.isOk()) {
              return {
                success: false,
                error: handleResult.error.message || 'Command handler failed'
              } as any;
            }
            // If OK, the result value should be the events array
            const handlerEvents = handleResult.value;
            
            if (Array.isArray(handlerEvents)) {
              events.push(...handlerEvents);
            }
          }
        } else {
          return {
            success: false,
            error: 'Command handle function not found'
          } as any;
        }
      } catch (error) {
        return {
          success: false,
          error: error instanceof Error ? error.message : 'Command handler failed'
        } as any;
      }
      
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
        const id = crypto.randomUUID();
        const timestamp = new Date().toISOString();
        // Get event type from the payload's type property (created by defineEvent)
        const eventType = eventPayload.type || eventPayload.constructor.name;
        
        // Create event document with both lowercase (for TypeScript) and uppercase (for C#) fields
        const eventDoc: SerializableEventDocument = {
          // Lowercase fields for TypeScript actors
          id: id,
          sortableUniqueId: sortableUniqueId.toString(),
          payload: eventPayload,
          eventType: eventType,
          aggregateId: partitionKeys.aggregateId,
          partitionKeys: partitionKeys,
          version: version++,
          createdAt: timestamp,
          metadata: {
            causationId: (metadata as any)?.causationId || '',
            correlationId: (metadata as any)?.correlationId || '',
            executedUser: (metadata as any)?.executedUser || 'system'
          },
          
          // Uppercase fields for C# compatibility
          Id: id,
          SortableUniqueId: sortableUniqueId.toString(),
          Version: version - 1, // Use the same version number
          AggregateId: partitionKeys.aggregateId,
          AggregateGroup: partitionKeys.group || 'default',
          RootPartitionKey: partitionKeys.rootPartitionKey || 'default',
          PayloadTypeName: eventType,
          TimeStamp: timestamp,
          PartitionKey: partitionKeys.partitionKey || '',
          CausationId: (metadata as any)?.causationId || '',
          CorrelationId: (metadata as any)?.correlationId || '',
          ExecutedUser: (metadata as any)?.executedUser || 'system',
          CompressedPayloadJson: Buffer.from(JSON.stringify(eventPayload)).toString('base64'),
          PayloadAssemblyVersion: '0.0.0.0'
        };
        
        eventDocuments.push(eventDoc);
      }
      
      // Call AggregateEventHandlerActor in the separate service
      const eventHandlerActorId = `${partitionKeys.group}-${partitionKeys.aggregateId}-${partitionKeys.rootPartitionKey || 'default'}`;
      
      // Create proxy for AggregateEventHandlerActor (in separate service)
      const eventHandlerActor = this.actorProxyFactory.createActorProxy(
        new ActorId(eventHandlerActorId),
        'AggregateEventHandlerActor'
      ) as any;
      
      const appendResult = await eventHandlerActor.appendEventsAsync(
        currentState?.lastSortableUniqueId || '',
        eventDocuments
      );
      
      if (!appendResult || !appendResult.isSuccess) {
        return {
          success: false,
          error: appendResult?.error || 'Failed to append events'
        } as any;
      }
      
      console.log('[AggregateActorImpl] Events saved successfully');
      
      // Update cached state with new events
      if (this._currentAggregate && projector) {
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
      } else if (!this._currentAggregate) {
        // If we don't have cached state yet, load it now with the new events
        const updatedState = await this.loadAggregateStateAsync(partitionKeys, projector as unknown as IAggregateProjector<any>);
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
      
      return response;
      
    } catch (error) {
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
      const eventHandlerActorId = `${partitionKeys.group}-${partitionKeys.aggregateId}-${partitionKeys.rootPartitionKey || 'default'}`;
      
      // Create proxy for AggregateEventHandlerActor (in separate service)
      const eventHandlerActor = this.actorProxyFactory.createActorProxy(
        new ActorId(eventHandlerActorId),
        'AggregateEventHandlerActor'
      ) as any;
      
      // Get all events (we'll optimize with delta loading later)
      const events = await eventHandlerActor.getAllEventsAsync();
      
      if (!events || events.length === 0) {
        this._currentAggregate = null;
        this._lastLoadedSortableUniqueId = '';
        return null;
      }
      
      // Apply events using projector
      let aggregate: any = null;
      let lastSortableUniqueId: string = '';
      
      // Initialize aggregate state using projector
      if (projector && projector.getInitialState) {
        aggregate = projector.getInitialState(partitionKeys);
      } else {
        aggregate = {};
      }
      
      for (const eventDoc of (events as any[])) {
        // Handle serialization differences like in multi-projector-actor
        const eventType = eventDoc.PayloadTypeName || eventDoc.eventType;
        const aggregateId = eventDoc.AggregateId || eventDoc.aggregateId || partitionKeys.aggregateId;
        const sortableIdStr = eventDoc.SortableUniqueId || eventDoc.sortableUniqueId || eventDoc.id;
        
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
        
        // Apply event to aggregate
        try {
          const applyResult = projector.project(aggregate, event);
        
        if (applyResult.isOk()) {
          aggregate = applyResult.value;
          lastSortableUniqueId = eventDoc.sortableUniqueId;
        }
        } catch (projError) {
          // Skip events that fail projection
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
      
      return aggregate;
    } catch (error) {
      return null;
    }
  }

  /**
   * Get aggregate state
   */
  async getAggregateStateAsync<
    TPayload extends ITypedAggregatePayload = ITypedAggregatePayload
  >(): Promise<Aggregate<TPayload> | null> {
    // Extract partition keys from actor ID if not already set
    let partitionKeys: PartitionKeys;
    let projector: IAggregateProjector<any>;
    
    if (this.currentPartitionKeysAndProjector) {
      partitionKeys = this.currentPartitionKeysAndProjector.partitionKeys;
      const foundProjector = this.domainTypes.projectorTypes.getProjectorByAggregateType(partitionKeys.group || 'Unknown');
      if (!foundProjector) {
        throw new Error(`Projector not found for aggregate type: ${partitionKeys.group || 'Unknown'}`);
      }
      projector = foundProjector as unknown as IAggregateProjector<any>;
    } else {
      // Parse actor ID: "rootPartition@group@aggregateId=projectorType"
      const parts = this.actorId.split('@');
      const lastPart = parts[parts.length - 1];
      const [aggregateId, projectorType] = lastPart.split('=');
      const group = parts[1];
      const rootPartition = parts[0];
      
      partitionKeys = new PartitionKeys(aggregateId, group, rootPartition);
      const foundProjector2 = this.domainTypes.projectorTypes.getProjectorByAggregateType(group);
      if (!foundProjector2) {
        throw new Error(`Projector not found for aggregate type: ${group}`);
      }
      projector = foundProjector2 as unknown as IAggregateProjector<any>;
      
    }
    
    if (!projector) {
      return null;
    }
    
    return this.loadAggregateStateAsync(partitionKeys, projector as IAggregateProjector<any>);
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
    this.hasUnsavedChanges = false;
    // Actual state saving logic would go here
  }

  /**
   * Rebuild state from events
   */
  async rebuildStateAsync(): Promise<void> {
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
      throw error;
    }
  }
}