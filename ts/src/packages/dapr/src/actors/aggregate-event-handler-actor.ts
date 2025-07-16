import { AbstractActor, ActorId, DaprClient } from '@dapr/dapr';
import type { PartitionKeys } from '@sekiban/core';
// @ts-ignore - These are exported from core
import type { IEventStore } from '@sekiban/core';
// @ts-ignore - These are exported from core
import { EventRetrievalInfo, AggregateGroupStream } from '@sekiban/core';
import { getDaprCradle } from '../container/index.js';
import type {
  IAggregateEventHandlerActor,
  SerializableEventDocument,
  EventHandlingResponse,
  AggregateEventHandlerState,
  ActorPartitionInfo
} from './interfaces';

/**
 * Handles event persistence and retrieval for aggregate streams
 * Mirrors C# AggregateEventHandlerActor implementation
 */
export class AggregateEventHandlerActor extends AbstractActor 
  implements IAggregateEventHandlerActor {
  
  // Explicitly define actor type for Dapr
  static get actorType() { 
    return "AggregateEventHandlerActor"; 
  }
  
  // State keys
  private readonly HANDLER_STATE_KEY = "aggregateEventHandler";
  private readonly EVENTS_KEY = "aggregateEventDocuments";
  private readonly PARTITION_INFO_KEY = "partitionInfo";
  
  private partitionInfo?: ActorPartitionInfo;
  private eventStore: IEventStore;
  private actorIdString!: string;
  
  constructor(daprClient: DaprClient, id: ActorId) {
    try {
      super(daprClient, id);
      console.log('[AggregateEventHandlerActor] Constructor called');
      
      // Extract actor ID string
      this.actorIdString = (id as any).id || String(id);
      
      // Get dependencies from Awilix container (same pattern as AggregateActor)
      const cradle = getDaprCradle();
      
      // Get eventStore from container
      this.eventStore = cradle.eventStore;
      
      console.log('[AggregateEventHandlerActor] Dependencies injected, eventStore:', !!this.eventStore);
    } catch (error) {
      console.error('[AggregateEventHandlerActor] Constructor error:', error);
      throw error;
    }
  }
  
  getDaprClient(): DaprClient {
    // Get DaprClient from the actor context
    return (this as any).client || new DaprClient();
  }
  
  /**
   * Actor activation
   */
  async onActivate(): Promise<void> {
    console.log('[AggregateEventHandlerActor] onActivate called for actor:', (this as any).id?.toString());
    // Don't load partition info on activation to avoid state access issues
    // It will be loaded on first method call instead
    console.log('[AggregateEventHandlerActor] Actor activated');
  }
  
  /**
   * Ensure partition info is saved (called on first method invocation)
   */
  private async ensurePartitionInfoSaved(): Promise<void> {
    try {
      const stateManager = await this.getStateManager();
      const [hasPartitionInfo] = await stateManager.tryGetState(this.PARTITION_INFO_KEY);
      
      if (!hasPartitionInfo && this.partitionInfo) {
        await stateManager.setState(this.PARTITION_INFO_KEY, this.partitionInfo);
        // Don't save state immediately, let it be saved with other state changes
        // await stateManager.saveState();
      }
    } catch (error) {
      console.warn('[AggregateEventHandlerActor] Could not save partition info:', error);
    }
  }

  /**
   * Append events with concurrency check
   */
  async appendEventsAsync(
    expectedLastSortableUniqueId: string,
    events: SerializableEventDocument[]
  ): Promise<EventHandlingResponse> {
    console.log('[AggregateEventHandlerActor] appendEventsAsync called with:');
    console.log('  - Actor ID:', (this as any).id?.toString());
    console.log('  - Expected last ID:', expectedLastSortableUniqueId);
    console.log('  - Events count:', events.length);
    console.log('  - Partition info:', JSON.stringify(this.partitionInfo));
    
    try {
      // Ensure partition info is saved on first method call
      await this.ensurePartitionInfoSaved();
      
      // Load current state
      const stateManager = await this.getStateManager();
      const [hasState, handlerState] = await stateManager.tryGetState(
        this.HANDLER_STATE_KEY
      );
      
      console.log('[AggregateEventHandlerActor] Current state:', hasState ? 'exists' : 'not found', handlerState);
      
      const currentLastId = (handlerState as any)?.lastSortableUniqueId || '';
      
      // Validate optimistic concurrency
      if (currentLastId !== expectedLastSortableUniqueId) {
        console.error('[AggregateEventHandlerActor] Concurrency conflict:', {
          expected: expectedLastSortableUniqueId,
          actual: currentLastId
        });
        return {
          isSuccess: false,
          error: `Concurrency conflict: expected ${expectedLastSortableUniqueId}, actual ${currentLastId}`
        };
      }
      
      // Load current events from state
      const currentEvents = await this.loadEventsFromStateAsync();
      
      // Append new events
      const allEvents = [...currentEvents, ...events];
      
      // Save to actor state
      await stateManager.setState(this.EVENTS_KEY, allEvents);
      
      // Save to external storage
      if (this.partitionInfo) {
        console.log('[AggregateEventHandlerActor] Saving to event store:', {
          partitionKeys: this.partitionInfo.partitionKeys,
          eventCount: events.length
        });
        
        try {
          const deserializedEvents = events.map(e => this.deserializeEvent(e));
          await this.eventStore.saveEvents(deserializedEvents);
          console.log('[AggregateEventHandlerActor] Events saved to event store successfully');
        } catch (saveError) {
          console.error('[AggregateEventHandlerActor] Failed to save to event store:', saveError);
          throw saveError;
        }
      } else {
        console.warn('[AggregateEventHandlerActor] No partition info available, skipping event store save');
      }
      
      // Update metadata
      const lastEvent = events[events.length - 1];
      console.log('[AggregateEventHandlerActor] Last event:', JSON.stringify(lastEvent));
      const newLastId = lastEvent.sortableUniqueId || lastEvent.SortableUniqueId;
      console.log('[AggregateEventHandlerActor] newLastId extracted:', newLastId);
      const newState: AggregateEventHandlerState = {
        lastSortableUniqueId: newLastId,
        eventCount: allEvents.length
      };
      
      await stateManager.setState(this.HANDLER_STATE_KEY, newState);
      
      // Save all state changes together
      try {
        await stateManager.saveState();
      } catch (saveError) {
        console.error('[AggregateEventHandlerActor] Failed to save state:', saveError);
        // Try alternative approach - return success if events were saved to external store
        if (this.partitionInfo) {
          console.warn('[AggregateEventHandlerActor] State save failed but events saved to external store, returning success');
          return {
            isSuccess: true,
            lastSortableUniqueId: newLastId
          };
        }
        throw saveError;
      }
      
      // Publish events to Dapr pub/sub
      try {
        const cradle = getDaprCradle();
        const daprClient = cradle.daprClient || this.getDaprClient();
        const pubSubName = cradle.configuration?.pubSubName || 'pubsub';
        const topicName = cradle.configuration?.eventTopicName || 'sekiban-events';
        
        // Publish each event
        for (const event of events) {
          // Extract only the C# compatible fields
          const publishEvent = {
            Id: event.Id,
            SortableUniqueId: event.SortableUniqueId,
            Version: event.Version,
            AggregateId: event.AggregateId,
            AggregateGroup: event.AggregateGroup,
            RootPartitionKey: event.RootPartitionKey,
            PayloadTypeName: event.PayloadTypeName,
            TimeStamp: event.TimeStamp,
            PartitionKey: event.PartitionKey,
            CausationId: event.CausationId,
            CorrelationId: event.CorrelationId,
            ExecutedUser: event.ExecutedUser,
            CompressedPayloadJson: event.CompressedPayloadJson,
            PayloadAssemblyVersion: event.PayloadAssemblyVersion
          };
          
          console.log(`[AggregateEventHandlerActor] Publishing event to pub/sub:`, {
            pubSubName,
            topicName,
            eventType: publishEvent.PayloadTypeName,
            aggregateId: publishEvent.AggregateId
          });
          
          await daprClient.pubsub.publish(pubSubName, topicName, publishEvent);
        }
        
        console.log(`[AggregateEventHandlerActor] Published ${events.length} events to pub/sub`);
      } catch (pubsubError) {
        // Log but don't fail - pub/sub is not critical for event storage
        console.error('[AggregateEventHandlerActor] Failed to publish events to pub/sub:', pubsubError);
      }
      
      try {
        console.log('[AggregateEventHandlerActor] After pub/sub block, newLastId:', newLastId);
      } catch (e) {
        console.error('[AggregateEventHandlerActor] Error accessing newLastId:', e);
      }
      
      console.log('[AggregateEventHandlerActor] About to return success response:', {
        isSuccess: true,
        lastSortableUniqueId: newLastId
      });
      
      const response = {
        isSuccess: true,
        lastSortableUniqueId: newLastId
      };
      
      console.log('[AggregateEventHandlerActor] Actually returning response now');
      return response;
    } catch (error) {
      console.error('[AggregateEventHandlerActor] Error in appendEventsAsync:', error);
      return {
        isSuccess: false,
        error: error instanceof Error ? error.message : 'Unknown error'
      };
    }
  }
  
  /**
   * Get events after a specific point
   */
  async getDeltaEventsAsync(
    fromSortableUniqueId: string,
    limit: number
  ): Promise<SerializableEventDocument[]> {
    const allEvents = await this.loadEventsFromStateAsync();
    
    // Find the index of the fromSortableUniqueId
    const fromIndex = allEvents.findIndex(
      e => e.sortableUniqueId === fromSortableUniqueId
    );
    
    if (fromIndex === -1) {
      // If not found, return all events (safety fallback)
      return allEvents.slice(0, limit);
    }
    
    // Return events after the specified ID, up to limit
    return allEvents.slice(fromIndex + 1, fromIndex + 1 + limit);
  }
  
  /**
   * Get all events
   */
  async getAllEventsAsync(): Promise<SerializableEventDocument[]> {
    console.log('[AggregateEventHandlerActor] getAllEventsAsync called for actor:', (this as any).id?.toString());
    try {
      // Load partition info if not already loaded
      if (!this.partitionInfo) {
        await this.loadPartitionInfoAsync();
      }
      
      // Ensure partition info is saved on first method call
      await this.ensurePartitionInfoSaved();
      
      const events = await this.loadEventsFromStateAsync();
      console.log('[AggregateEventHandlerActor] Returning', events.length, 'events');
      return events;
    } catch (error) {
      console.error('[AggregateEventHandlerActor] Error in getAllEventsAsync:', error);
      throw error;
    }
  }
  
  /**
   * Get last event ID
   */
  async getLastSortableUniqueIdAsync(): Promise<string> {
    const stateManager = await this.getStateManager();
    const [hasState, handlerState] = await stateManager.tryGetState(
      this.HANDLER_STATE_KEY
    );
    
    return (handlerState as AggregateEventHandlerState)?.lastSortableUniqueId || '';
  }
  
  /**
   * Register projector (currently no-op as per C# implementation)
   */
  async registerProjectorAsync(projectorKey: string): Promise<void> {
    // No-op - placeholder for future functionality
  }
  
  /**
   * Load events from state with external storage fallback
   */
  private async loadEventsFromStateAsync(): Promise<SerializableEventDocument[]> {
    console.log('[AggregateEventHandlerActor] loadEventsFromStateAsync called');
    
    const stateManager = await this.getStateManager();
    console.log('[AggregateEventHandlerActor] Got state manager');
    
    const [hasEvents, events] = await stateManager.tryGetState(
      this.EVENTS_KEY
    );
    
    console.log('[AggregateEventHandlerActor] State check:', {
      hasEvents,
      eventsCount: events ? (events as any).length : 0
    });
    
    if (!hasEvents || !events || (events as any).length === 0) {
      // Fallback to external storage
      if (this.partitionInfo) {
        const eventRetrievalInfo = EventRetrievalInfo.fromPartitionKeys(this.partitionInfo.partitionKeys);
        const externalEventsResult = await this.eventStore.getEvents(eventRetrievalInfo);
        const externalEvents = externalEventsResult.isOk() ? externalEventsResult.value : [];
        
        const serializedEvents = externalEvents.map((e: any) => this.serializeEvent(e));
        
        // Cache in state for next time
        if (serializedEvents.length > 0) {
          await stateManager.setState(this.EVENTS_KEY, serializedEvents);
          
          // Update metadata
          const lastEvent = serializedEvents[serializedEvents.length - 1];
          const state: AggregateEventHandlerState = {
            lastSortableUniqueId: lastEvent.sortableUniqueId,
            eventCount: serializedEvents.length
          };
          await stateManager.setState(this.HANDLER_STATE_KEY, state);
          await stateManager.saveState();
        }
        
        return serializedEvents;
      }
      
      return [];
    }
    
    return (events as SerializableEventDocument[]) || [];
  }
  
  /**
   * Load partition info from state or actor ID
   */
  private async loadPartitionInfoAsync(): Promise<void> {
    try {
      const stateManager = await this.getStateManager();
      const [hasPartitionInfo, partitionInfo] = await stateManager.tryGetState(
        this.PARTITION_INFO_KEY
      );
      
      if (hasPartitionInfo && partitionInfo) {
        this.partitionInfo = partitionInfo as ActorPartitionInfo;
      } else {
        // Extract from actor ID but don't save during onActivate
        // Dapr might not have the actor instance ready yet
        this.partitionInfo = this.getPartitionInfoFromActorId();
        // We'll save it on first actual method call instead
      }
    } catch (error) {
      console.warn('[AggregateEventHandlerActor] Error loading partition info, using extracted info:', error);
      // Fallback to extracting from actor ID
      this.partitionInfo = this.getPartitionInfoFromActorId();
    }
  }
  
  /**
   * Extract partition info from actor ID
   */
  private getPartitionInfoFromActorId(): ActorPartitionInfo {
    // Actor ID format: "aggregateType:aggregateId:rootPartition"
    const actorId = (this as any).id.toString();
    console.log('[AggregateEventHandlerActor] Extracting partition info from actor ID:', actorId);
    const idParts = actorId.split(':');
    
    const partitionInfo: ActorPartitionInfo = {
      partitionKeys: {
        aggregateId: idParts[1] || '',
        group: idParts[0] || '',
        rootPartitionKey: idParts[2] || 'default',
        partitionKey: `${idParts[0] || ''}:${idParts[1] || ''}:${idParts[2] || 'default'}`
      } as PartitionKeys,
      aggregateType: idParts[0] || '',
      projectorType: '' // Not used in event handler
    };
    
    console.log('[AggregateEventHandlerActor] Extracted partition info:', JSON.stringify(partitionInfo));
    return partitionInfo;
  }
  
  /**
   * Serialize event for storage
   */
  private serializeEvent(event: any): SerializableEventDocument {
    // Convert to proper SerializableEventDocument format for pub/sub
    const payloadJson = JSON.stringify(event.payload || {});
    const payloadBase64 = Buffer.from(payloadJson).toString('base64');
    
    return {
      // Use uppercase field names to match C# format
      Id: event.id?.value || event.id || '',
      SortableUniqueId: event.sortableUniqueId || event.id?.value || '',
      Version: event.version || 1,
      
      // Partition keys
      AggregateId: event.aggregateId || event.partitionKeys?.aggregateId || '',
      AggregateGroup: event.aggregateType || event.partitionKeys?.group || 'default',
      RootPartitionKey: event.partitionKeys?.rootPartitionKey || 'default',
      
      // Event info
      PayloadTypeName: event.payload?.constructor?.name || event.eventType || 'Unknown',
      TimeStamp: event.createdAt instanceof Date ? event.createdAt.toISOString() : event.createdAt,
      PartitionKey: event.partitionKeys?.partitionKey || event.partitionKeys?.toString?.() || '',
      
      // Metadata
      CausationId: event.metadata?.causationId || '',
      CorrelationId: event.metadata?.correlationId || '',
      ExecutedUser: event.metadata?.executedUser || event.metadata?.userId || '',
      
      // Payload (not compressed for now)
      CompressedPayloadJson: payloadBase64,
      
      // Version
      PayloadAssemblyVersion: '0.0.0.0',
      
      // Keep old format fields for backward compatibility
      id: event.id,
      sortableUniqueId: event.sortableUniqueId,
      payload: event.payload,
      eventType: event.payload?.constructor?.name || event.eventType || 'Unknown',
      aggregateId: event.aggregateId,
      partitionKeys: event.partitionKeys,
      version: event.version,
      createdAt: event.createdAt instanceof Date ? event.createdAt.toISOString() : event.createdAt,
      metadata: event.metadata || {}
    } as any;
  }
  
  /**
   * Deserialize event from storage
   */
  private deserializeEvent(serialized: SerializableEventDocument): any {
    return {
      ...serialized,
      createdAt: new Date(serialized.createdAt)
    };
  }
}