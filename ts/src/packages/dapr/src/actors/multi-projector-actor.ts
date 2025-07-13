import { AbstractActor, ActorId, DaprClient } from '@dapr/dapr';
import type { IEventStore, EventDocument } from '@sekiban/core';
import { EventRetrievalInfo, SortableIdCondition, SortableUniqueId, OptionalValue, IEvent, ISortableIdCondition } from '@sekiban/core';
import type {
  IMultiProjectorActor,
  SerializableQuery,
  SerializableListQuery,
  QueryResponse,
  ListQueryResponse,
  MultiProjectionState,
  DaprEventEnvelope,
  BufferedEvent,
  SerializableEventDocument
} from './interfaces';
import { getDaprCradle } from '../container/index.js';
import { ungzip } from 'node:zlib';
import { promisify } from 'node:util';

const ungzipAsync = promisify(ungzip);

/**
 * Handles cross-aggregate projections and queries over multiple aggregates
 * Mirrors C# MultiProjectorActor implementation
 */
export class MultiProjectorActor extends AbstractActor implements IMultiProjectorActor {
  private safeState?: MultiProjectionState;     // State older than 7 seconds
  private unsafeState?: MultiProjectionState;   // Recent state including buffer
  private eventBuffer: BufferedEvent[] = [];
  private lastProcessedTimestamp?: Date;
  
  // Reminder names
  private readonly SNAPSHOT_REMINDER = "snapshot";
  private readonly EVENT_CHECK_REMINDER = "eventCheck";
  
  // Timers as fallback
  private snapshotTimer?: NodeJS.Timeout;
  private eventCheckTimer?: NodeJS.Timeout;
  
  // Configuration
  private readonly SAFE_WINDOW_MS = 7000;        // 7 seconds
  private readonly SNAPSHOT_INTERVAL_MS = 300000; // 5 minutes
  private readonly EVENT_CHECK_INTERVAL_MS = 1000; // 1 second
  
  // State keys
  private readonly PROJECTION_STATE_KEY = "projectionState";
  private readonly PROCESSED_EVENTS_KEY = "processedEvents";
  
  private eventStore: IEventStore;
  private projectorType: string;
  
  constructor(
    daprClient: DaprClient,
    id: ActorId
  ) {
    super(daprClient, id);
    
    // Get dependencies from container
    try {
      const cradle = getDaprCradle();
      this.eventStore = cradle.eventStore;
      
      // Extract projector type from actor ID
      const actorIdStr = id.toString();
      const parts = actorIdStr.split('-');
      this.projectorType = parts.length > 1 ? parts[1] : 'unknown';
      
      console.log(`[MultiProjectorActor] Created for ${actorIdStr}, projectorType: ${this.projectorType}`);
    } catch (error) {
      console.error('[MultiProjectorActor] Failed to get dependencies from container:', error);
      // Create a dummy event store that returns empty results
      this.eventStore = {
        getEvents: async () => ({ isOk: () => true, isErr: () => false, value: [], error: null } as any),
        appendEvents: async () => ({ isOk: () => true, isErr: () => false, value: [], error: null } as any),
        initialize: async () => ({ isOk: () => true, isErr: () => false, value: undefined, error: null } as any),
        close: async () => ({ isOk: () => true, isErr: () => false, value: undefined, error: null } as any)
      } as any;
      this.projectorType = 'unknown';
    }
  }
  
  /**
   * Actor activation - set up reminders/timers
   */
  async onActivate(): Promise<void> {
    try {
      // Register reminders - AbstractActor provides registerReminder method
      await this.registerReminder(
        this.SNAPSHOT_REMINDER,
        Buffer.from(""),
        "PT5M",  // 5 minutes
        "PT5M"
      );
      
      await this.registerReminder(
        this.EVENT_CHECK_REMINDER,
        Buffer.from(""),
        "PT1S",  // 1 second
        "PT1S"
      );
      
      console.log(`[MultiProjectorActor] Registered reminders for ${this.projectorType}`);
    } catch (error) {
      // Fall back to timers if reminders fail
      console.warn('[MultiProjectorActor] Failed to register reminders, falling back to timers:', error);
      
      this.snapshotTimer = setInterval(
        () => this.handleSnapshotReminder(),
        this.SNAPSHOT_INTERVAL_MS
      );
      
      this.eventCheckTimer = setInterval(
        () => this.handleEventCheckReminder(),
        this.EVENT_CHECK_INTERVAL_MS
      );
    }
    
    // Load initial state
    await this.loadStateAsync();
    
    // Catch up from event store (following C# MultiProjectorGrain pattern)
    console.log('[MultiProjectorActor] Catching up from event store...');
    await this.catchUpFromStoreAsync();
  }
  
  /**
   * Actor deactivation - clean up
   */
  async onDeactivate(): Promise<void> {
    // Clean up timers
    if (this.snapshotTimer) {
      clearInterval(this.snapshotTimer);
    }
    if (this.eventCheckTimer) {
      clearInterval(this.eventCheckTimer);
    }
    
    // Save final state
    if (this.safeState) {
      await this.persistStateAsync(this.safeState);
    }
  }
  
  /**
   * Execute single-item query
   */
  async queryAsync(query: SerializableQuery): Promise<QueryResponse> {
    try {
      await this.flushBuffer();
      
      // Use safe state for queries
      const state = this.safeState || await this.buildStateAsync();
      
      console.log('[MultiProjectorActor] Executing query:', {
        queryType: query.queryType,
        projectorType: this.projectorType,
        projectionsCount: Object.keys(state.projections).length
      });
      
      // Basic implementation: return first matching projection based on query payload
      const projections = state.projections;
      
      // If query has an ID field, try to find by ID
      if (query.payload?.id) {
        const projection = projections[query.payload.id];
        if (projection) {
          return {
            isSuccess: true,
            data: projection
          };
        }
      }
      
      // Otherwise, search through all projections
      const projectionEntries = Object.entries(projections);
      for (const [id, projection] of projectionEntries) {
        // Simple matching: check if projection matches query filters
        if (this.matchesQuery(projection, query.payload)) {
          return {
            isSuccess: true,
            data: projection
          };
        }
      }
      
      // No matching projection found
      return {
        isSuccess: true,
        data: null
      };
    } catch (error) {
      console.error('[MultiProjectorActor] Query error:', error);
      return {
        isSuccess: false,
        error: error instanceof Error ? error.message : 'Unknown error'
      };
    }
  }
  
  /**
   * Simple query matcher
   */
  private matchesQuery(projection: any, queryPayload: any): boolean {
    if (!queryPayload || Object.keys(queryPayload).length === 0) {
      return true; // No filters, match all
    }
    
    // Check each filter property
    for (const [key, value] of Object.entries(queryPayload)) {
      if (key === 'id') continue; // Already handled
      
      // Simple equality check
      if (projection[key] !== value) {
        return false;
      }
    }
    
    return true;
  }
  
  /**
   * Execute list query
   */
  async queryListAsync(query: SerializableListQuery): Promise<ListQueryResponse> {
    try {
      await this.flushBuffer();
      
      // Use safe state for queries
      const state = this.safeState || await this.buildStateAsync();
      
      console.log('[MultiProjectorActor] Executing list query:', {
        queryType: query.queryType,
        projectorType: this.projectorType,
        skip: query.skip,
        take: query.take,
        projectionsCount: Object.keys(state.projections).length
      });
      
      // Get all matching projections
      const projections = state.projections;
      const matchingProjections: any[] = [];
      
      // Filter projections based on query
      for (const [id, projection] of Object.entries(projections)) {
        if (this.matchesQuery(projection, query.payload)) {
          matchingProjections.push({ ...projection, id });
        }
      }
      
      // Apply pagination
      const skip = query.skip || 0;
      const take = query.take || 10;
      const paginatedItems = matchingProjections.slice(skip, skip + take);
      
      return {
        isSuccess: true,
        items: paginatedItems,
        totalCount: matchingProjections.length
      };
    } catch (error) {
      console.error('[MultiProjectorActor] List query error:', error);
      return {
        isSuccess: false,
        error: error instanceof Error ? error.message : 'Unknown error'
      };
    }
  }
  
  /**
   * Check if event has been processed
   */
  async isSortableUniqueIdReceived(sortableUniqueId: string): Promise<boolean> {
    // Check buffer
    if (this.eventBuffer.some(e => e.event.sortableUniqueId === sortableUniqueId)) {
      return true;
    }
    
    // Check processed events in state
    const processedEvents = await this.getProcessedEventsAsync();
    return processedEvents.has(sortableUniqueId);
  }
  
  /**
   * Build current state from buffer
   */
  async buildStateAsync(): Promise<MultiProjectionState> {
    await this.flushBuffer();
    return this.unsafeState || this.createEmptyState();
  }
  
  /**
   * Rebuild state from scratch
   */
  async rebuildStateAsync(): Promise<void> {
    // Clear buffer and states
    this.eventBuffer = [];
    this.safeState = undefined;
    this.unsafeState = undefined;
    
    // Load all events from store
    const retrievalInfo = new EventRetrievalInfo(
      OptionalValue.empty<string>(),
      OptionalValue.empty<any>(),
      OptionalValue.empty<string>(),
      SortableIdCondition.none(),
      OptionalValue.fromValue(10000) // Get up to 10k events
    );
    
    const eventsResult = await this.eventStore.getEvents(retrievalInfo);
    if (eventsResult.isErr()) {
      console.error('Failed to load events for rebuild:', eventsResult.error);
      return;
    }
    
    const events = eventsResult.value;
    
    // Build new state
    const newState = this.createEmptyState();
    
    // Apply all events
    for (const event of events) {
      const serializedEvent: SerializableEventDocument = {
        id: event.id.value,
        sortableUniqueId: event.id.value,
        payload: event.payload,
        eventType: event.type,
        aggregateId: event.aggregateId,
        partitionKeys: event.partitionKeys,
        version: event.version,
        createdAt: typeof event.createdAt === 'string' ? event.createdAt : event.createdAt.toISOString(),
        metadata: event.metadata || {},
        aggregateType: event.aggregateType
      };
      
      newState.projections = await this.applyEventToProjections(
        newState.projections,
        serializedEvent
      );
      newState.lastProcessedEventId = event.id.value;
      newState.lastProcessedTimestamp = event.createdAt.toISOString();
      newState.version++;
    }
    
    this.safeState = newState;
    this.unsafeState = { ...newState };
    
    // Persist rebuilt state
    await this.persistStateAsync(newState);
  }
  
  /**
   * Handle published event from PubSub
   */
  async handlePublishedEvent(envelope: DaprEventEnvelope): Promise<void> {
    // Check if already processed
    if (await this.isSortableUniqueIdReceived(envelope.event.sortableUniqueId)) {
      return;
    }
    
    // Add to buffer with timestamp
    this.eventBuffer.push({
      event: envelope.event,
      receivedAt: new Date()
    });
    
    // Sort buffer by sortableUniqueId
    this.eventBuffer.sort((a, b) => 
      a.event.sortableUniqueId.localeCompare(b.event.sortableUniqueId)
    );
    
    // Trim buffer if too large (keep last 10000 events)
    if (this.eventBuffer.length > 10000) {
      this.eventBuffer = this.eventBuffer.slice(-10000);
    }
  }
  
  /**
   * Reminder handling - Dapr expects this method name
   */
  async receiveReminder(
    reminderName: string,
    state: Buffer,
    dueTime: string,
    period: string
  ): Promise<void> {
    switch (reminderName) {
      case this.SNAPSHOT_REMINDER:
        await this.handleSnapshotReminder();
        break;
      case this.EVENT_CHECK_REMINDER:
        await this.handleEventCheckReminder();
        break;
    }
  }
  
  /**
   * Alias for backward compatibility
   */
  async receiveReminderAsync(
    reminderName: string,
    state: Buffer,
    dueTime: string,
    period: string
  ): Promise<void> {
    return this.receiveReminder(reminderName, state, dueTime, period);
  }
  
  /**
   * Process buffered events
   */
  private async flushBuffer(): Promise<void> {
    const now = new Date();
    
    // Find events older than safe window
    const safeEvents = this.eventBuffer.filter(
      e => now.getTime() - e.receivedAt.getTime() > this.SAFE_WINDOW_MS
    );
    
    if (safeEvents.length > 0) {
      // Update safe state with safe events
      const newSafeState = this.safeState || this.createEmptyState();
      
      for (const bufferedEvent of safeEvents) {
        newSafeState.projections = await this.applyEventToProjections(
          newSafeState.projections,
          bufferedEvent.event
        );
        newSafeState.lastProcessedEventId = bufferedEvent.event.sortableUniqueId;
        newSafeState.lastProcessedTimestamp = bufferedEvent.event.createdAt;
        newSafeState.version++;
      }
      
      this.safeState = newSafeState;
      
      // Remove processed events from buffer
      this.eventBuffer = this.eventBuffer.filter(
        e => !safeEvents.includes(e)
      );
      
      // Track processed events
      await this.addProcessedEventsAsync(
        safeEvents.map(e => e.event.sortableUniqueId)
      );
    }
    
    // Always update unsafe state with all events
    const newUnsafeState = this.safeState 
      ? { ...this.safeState }
      : this.createEmptyState();
    
    // Apply remaining buffer events to unsafe state
    for (const bufferedEvent of this.eventBuffer) {
      newUnsafeState.projections = await this.applyEventToProjections(
        newUnsafeState.projections,
        bufferedEvent.event
      );
    }
    
    this.unsafeState = newUnsafeState;
  }
  
  /**
   * Handle snapshot reminder/timer
   */
  private async handleSnapshotReminder(): Promise<void> {
    if (this.safeState) {
      await this.persistStateAsync(this.safeState);
    }
  }
  
  /**
   * Handle event check reminder/timer
   */
  private async handleEventCheckReminder(): Promise<void> {
    // Catch up from external storage
    await this.catchUpFromStoreAsync();
    
    // Flush buffer
    await this.flushBuffer();
  }
  
  /**
   * Catch up from external event store
   */
  private async catchUpFromStoreAsync(): Promise<void> {
    const lastProcessedId = this.safeState?.lastProcessedEventId || '';
    
    try {
      // Create retrieval info to get all events after the last processed one
      let sortableIdCondition: ISortableIdCondition;
      
      if (lastProcessedId) {
        const lastIdResult = SortableUniqueId.fromString(lastProcessedId);
        if (lastIdResult.isOk()) {
          sortableIdCondition = SortableIdCondition.since(lastIdResult.value);
        } else {
          sortableIdCondition = SortableIdCondition.none();
        }
      } else {
        sortableIdCondition = SortableIdCondition.none();
      }
      
      // Create retrieval info for all events with the condition
      const retrievalInfo = new EventRetrievalInfo(
        OptionalValue.empty<string>(),
        OptionalValue.empty<any>(),
        OptionalValue.empty<string>(),
        sortableIdCondition,
        OptionalValue.fromValue(1000) // Batch size
      );
      
      // Load events using the proper interface
      const eventsResult = await this.eventStore.getEvents(retrievalInfo);
      
      if (eventsResult.isErr()) {
        console.error('[MultiProjectorActor] Failed to get events from store:', eventsResult.error);
        return;
      }
      
      const newEvents = eventsResult.value;
      console.log(`[MultiProjectorActor] Retrieved ${newEvents.length} events from store`);
      
      // Debug: Check the structure of the first event
      if (newEvents.length > 0) {
        const firstEvent = newEvents[0];
        console.log(`[MultiProjectorActor] First event structure:`, {
          hasType: 'type' in firstEvent,
          hasEventType: 'eventType' in firstEvent,
          type: (firstEvent as any).type,
          eventType: (firstEvent as any).eventType,
          aggregateType: firstEvent.aggregateType,
          aggregateId: firstEvent.aggregateId,
          payload: firstEvent.payload
        });
      }
      
      // Add to buffer if not already present
      for (const event of newEvents) {
        const sortableId = event.id.value;
        
        if (!await this.isSortableUniqueIdReceived(sortableId)) {
          // Events from store already have the right structure, just need to convert to SerializableEventDocument
          let createdAtStr: string;
          if (typeof event.createdAt === 'string') {
            createdAtStr = event.createdAt;
          } else if (event.createdAt instanceof Date) {
            createdAtStr = event.createdAt.toISOString();
          } else if (event.createdAt && typeof event.createdAt.toISOString === 'function') {
            createdAtStr = event.createdAt.toISOString();
          } else {
            console.warn('[MultiProjectorActor] Invalid createdAt format:', event.createdAt);
            createdAtStr = new Date().toISOString();
          }
          
          const serializedEvent: SerializableEventDocument = {
            id: event.id.value,
            sortableUniqueId: event.id.value,
            payload: event.payload,
            eventType: event.type || event.eventType,  // Support both field names
            aggregateId: event.aggregateId,
            partitionKeys: event.partitionKeys,
            version: event.version,
            createdAt: createdAtStr,
            metadata: event.metadata || {},
            aggregateType: event.aggregateType
          };
          
          this.eventBuffer.push({
            event: serializedEvent,
            receivedAt: new Date()
          });
        }
      }
      
      // Re-sort buffer
      this.eventBuffer.sort((a, b) => 
        a.event.sortableUniqueId.localeCompare(b.event.sortableUniqueId)
      );
    } catch (error) {
      console.error('Failed to catch up from store:', error);
    }
  }
  
  /**
   * Load state from actor storage
   */
  private async loadStateAsync(): Promise<void> {
    const stateManager = await this.getStateManager();
    const [hasState, state] = await stateManager.tryGetState<MultiProjectionState>(
      this.PROJECTION_STATE_KEY
    );
    
    if (hasState && state) {
      this.safeState = state;
      this.unsafeState = { ...state };
    }
  }
  
  /**
   * Persist state to actor storage
   */
  private async persistStateAsync(state: MultiProjectionState): Promise<void> {
    const stateManager = await this.getStateManager();
    await stateManager.setState(
      this.PROJECTION_STATE_KEY,
      state
    );
  }
  
  /**
   * Get processed events set
   */
  private async getProcessedEventsAsync(): Promise<Set<string>> {
    const stateManager = await this.getStateManager();
    const [hasEvents, events] = await stateManager.tryGetState<string[]>(
      this.PROCESSED_EVENTS_KEY
    );
    
    return new Set(events || []);
  }
  
  /**
   * Add processed events
   */
  private async addProcessedEventsAsync(eventIds: string[]): Promise<void> {
    const processed = await this.getProcessedEventsAsync();
    
    for (const id of eventIds) {
      processed.add(id);
    }
    
    // Keep only last 100000 events to prevent unbounded growth
    const processedArray = Array.from(processed);
    if (processedArray.length > 100000) {
      const trimmed = processedArray.slice(-100000);
      const stateManager = await this.getStateManager();
      await stateManager.setState(
        this.PROCESSED_EVENTS_KEY,
        trimmed
      );
    } else {
      await stateManager.setState(
        this.PROCESSED_EVENTS_KEY,
        processedArray
      );
    }
  }
  
  /**
   * Apply event to projections
   */
  private async applyEventToProjections(
    projections: Record<string, any>,
    event: SerializableEventDocument
  ): Promise<Record<string, any>> {
    try {
      // Get the domain types from container
      const cradle = getDaprCradle();
      const domainTypes = cradle.domainTypes;
      
      // Extract the projector name from the actor ID
      // Format: aggregatelistprojector-{projectorname}
      const actorIdStr = this.id.toString();
      const parts = actorIdStr.split('-');
      let projectorName = parts.length > 1 ? parts[1] : '';
      
      // The projectorName might be like 'taskprojector', but we need 'Task' for aggregateTypeName
      // Common pattern: remove 'projector' suffix and capitalize
      if (projectorName.endsWith('projector')) {
        projectorName = projectorName.slice(0, -9); // Remove 'projector'
        projectorName = projectorName.charAt(0).toUpperCase() + projectorName.slice(1); // Capitalize
      }
      
      console.log(`[MultiProjectorActor] Applying event to projections:`, {
        projectorName,
        eventType: event.eventType,
        aggregateId: event.aggregateId,
        actorId: actorIdStr,
        payload: event.payload
      });
      
      // Find the projector in the registry
      let projectorInstance = null;
      
      if (domainTypes.projectorTypes && typeof domainTypes.projectorTypes.getProjectorTypes === 'function') {
        const projectorList = domainTypes.projectorTypes.getProjectorTypes();
        const projectorWrapper = projectorList.find(
          (p: any) => p.aggregateTypeName.toLowerCase() === projectorName.toLowerCase()
        );
        
        if (projectorWrapper) {
          projectorInstance = projectorWrapper.projector;
        }
      }
      
      if (!projectorInstance) {
        console.warn(`[MultiProjectorActor] Projector not found: ${projectorName}`);
        console.warn(`[MultiProjectorActor] Available projectors:`, 
          domainTypes.projectorTypes?.getProjectorTypes?.()?.map((p: any) => p.aggregateTypeName) || 'none'
        );
        return projections;
      }
      
      // Debug: Check projector structure
      console.log(`[MultiProjectorActor] Projector instance type:`, typeof projectorInstance);
      console.log(`[MultiProjectorActor] Projector has project method:`, typeof projectorInstance.project === 'function');
      console.log(`[MultiProjectorActor] Projector has getInitialState method:`, typeof projectorInstance.getInitialState === 'function');
      
      // Check if projector has projections
      if (projectorInstance.projections) {
        console.log(`[MultiProjectorActor] Projector has projections:`, Object.keys(projectorInstance.projections));
      }
      
      // Check if this projector can handle this event type
      // SerializableEventDocument uses PayloadTypeName for event type
      const eventType = event.PayloadTypeName;
      if (projectorInstance.canHandle && !projectorInstance.canHandle(eventType)) {
        // This projector doesn't handle this event type
        return projections;
      }
      
      // Get or create the projection for this aggregate
      const aggregateId = event.AggregateId;
      let currentProjection = projections[aggregateId];
      
      // If no projection exists, create initial state
      if (!currentProjection) {
        // Reconstruct partition keys from SerializableEventDocument
        const partitionKeys = {
          aggregateId: event.AggregateId,
          group: event.AggregateGroup || projectorName,
          rootPartitionKey: event.RootPartitionKey || 'default',
          partitionKey: event.PartitionKey || '',
          toString: () => event.PartitionKey || `${event.AggregateGroup}-${event.AggregateId}`
        };
        
        const initialAggregate = projectorInstance.getInitialState(partitionKeys);
        currentProjection = initialAggregate;
      }
      
      // Convert SerializableEventDocument to IEvent format
      const sortableIdResult = SortableUniqueId.fromString(event.SortableUniqueId);
      const sortableId = sortableIdResult.isOk() ? sortableIdResult.value : SortableUniqueId.create();
      
      // Decompress payload if needed
      let payload: any;
      try {
        if (event.CompressedPayloadJson) {
          // Check if it's actually compressed
          const payloadBuffer = Buffer.from(event.CompressedPayloadJson, 'base64');
          
          // Check for gzip header (1f 8b)
          if (payloadBuffer[0] === 0x1f && payloadBuffer[1] === 0x8b) {
            // It's gzipped, decompress it
            const decompressed = await ungzipAsync(payloadBuffer);
            const payloadJson = decompressed.toString('utf-8');
            payload = JSON.parse(payloadJson);
          } else {
            // Not gzipped, just base64 encoded JSON
            const payloadJson = payloadBuffer.toString('utf-8');
            payload = JSON.parse(payloadJson);
          }
        } else {
          // Fallback if not compressed
          payload = {};
        }
      } catch (error) {
        console.error('[MultiProjectorActor] Error decompressing payload:', error);
        payload = {};
      }
      
      // Reconstruct partition keys
      const partitionKeys = {
        aggregateId: event.AggregateId,
        group: event.AggregateGroup || projectorName,
        rootPartitionKey: event.RootPartitionKey || 'default',
        partitionKey: event.PartitionKey || '',
        toString: () => event.PartitionKey || `${event.AggregateGroup}-${event.AggregateId}`
      };
      
      const iEvent: IEvent = {
        id: sortableId,
        aggregateType: event.AggregateGroup || 'unknown',
        aggregateId: event.AggregateId,
        eventType: event.PayloadTypeName,  // PayloadTypeName is the event type!
        payload: payload,
        version: event.Version,
        partitionKeys: partitionKeys,
        sortableUniqueId: sortableId,
        timestamp: new Date(event.TimeStamp),
        metadata: {
          causationId: event.CausationId,
          correlationId: event.CorrelationId,
          userId: event.ExecutedUser,
          executedUser: event.ExecutedUser,
          timestamp: new Date(event.TimeStamp)
        },
        // Additional fields for IEvent interface
        partitionKey: event.PartitionKey,
        aggregateGroup: event.AggregateGroup,
        eventData: payload
      };
      
      // Project the event
      console.log(`[MultiProjectorActor] Projecting event:`, {
        currentProjectionPayload: currentProjection.payload,
        eventType: iEvent.eventType,
        eventPayload: iEvent.payload
      });
      
      const result = projectorInstance.project(currentProjection, iEvent);
      
      if (result.isOk()) {
        const newProjection = result.value;
        console.log(`[MultiProjectorActor] Projection result:`, {
          newPayload: newProjection.payload,
          version: newProjection.version,
          payloadType: newProjection.payload?.aggregateType || typeof newProjection.payload
        });
        
        // Update the projections map
        projections[aggregateId] = {
          id: aggregateId,
          aggregateType: newProjection.aggregateType,
          version: newProjection.version,
          lastEventId: event.id,
          lastSortableUniqueId: event.sortableUniqueId,
          payload: newProjection.payload,
          partitionKeys: newProjection.partitionKeys,
          createdAt: currentProjection.createdAt || new Date().toISOString(),
          updatedAt: new Date().toISOString()
        };
        
        console.log(`[MultiProjectorActor] Projection updated for aggregate: ${aggregateId}`);
      } else {
        console.error(`[MultiProjectorActor] Failed to project event:`, result.error);
      }
      
      return projections;
    } catch (error) {
      console.error('[MultiProjectorActor] Error in applyEventToProjections:', error);
      return projections;
    }
  }
  
  /**
   * Create empty state
   */
  private createEmptyState(): MultiProjectionState {
    return {
      projections: {},
      lastProcessedEventId: '',
      lastProcessedTimestamp: new Date().toISOString(),
      version: 0
    };
  }
  
  /**
   * Serialize event for storage
   */
  private serializeEvent(event: IEvent): SerializableEventDocument {
    return {
      id: event.id.value,
      sortableUniqueId: event.id.value,
      payload: event.payload,
      eventType: event.type,
      aggregateId: event.aggregateId,
      partitionKeys: event.partitionKeys,
      version: event.version,
      createdAt: typeof event.createdAt === 'string' ? event.createdAt : event.createdAt.toISOString(),
      metadata: event.metadata || {},
      aggregateType: event.aggregateType
    };
  }
  
  /**
   * Receive event from pub/sub
   */
  async receiveEventAsync(eventData: any): Promise<void> {
    console.log('[MultiProjectorActor] Received event from pub/sub:', {
      payloadTypeName: eventData?.PayloadTypeName,
      aggregateGroup: eventData?.AggregateGroup,
      aggregateId: eventData?.AggregateId,
      actorId: this.id.toString()
    });
    
    try {
      // Check if it's already in SerializableEventDocument format
      let serializedEvent: SerializableEventDocument;
      
      if (eventData.PayloadTypeName && eventData.CompressedPayloadJson) {
        // It's already in the correct format
        serializedEvent = eventData as SerializableEventDocument;
      } else {
        // Legacy format - convert to SerializableEventDocument
        console.warn('[MultiProjectorActor] Received event in legacy format, converting...');
        
        // Create a simple serialized event for backward compatibility
        const payload = eventData.payload || eventData;
        const payloadJson = JSON.stringify(payload);
        const payloadBase64 = Buffer.from(payloadJson).toString('base64');
        
        serializedEvent = {
          Id: eventData.id || SortableUniqueId.create().value,
          SortableUniqueId: eventData.sortableUniqueId || eventData.id || SortableUniqueId.create().value,
          Version: eventData.version || 1,
          
          // Partition keys
          AggregateId: eventData.aggregateId || '',
          AggregateGroup: eventData.aggregateType || eventData.aggregateGroup || 'default',
          RootPartitionKey: eventData.partitionKeys?.rootPartitionKey || 'default',
          
          // Event info
          PayloadTypeName: eventData.eventType || eventData.type || 'UnknownEvent',
          TimeStamp: eventData.createdAt || eventData.timestamp || new Date().toISOString(),
          PartitionKey: eventData.partitionKey || '',
          
          // Metadata
          CausationId: eventData.metadata?.causationId || '',
          CorrelationId: eventData.metadata?.correlationId || '',
          ExecutedUser: eventData.metadata?.executedUser || eventData.metadata?.userId || '',
          
          // Payload (not compressed in legacy format)
          CompressedPayloadJson: payloadBase64,
          
          // Version
          PayloadAssemblyVersion: '0.0.0.0'
        };
      }
      
      // Add to buffer
      this.eventBuffer.push({
        event: serializedEvent,
        receivedAt: new Date()
      });
      
      // If we're not already processing, flush immediately
      if (this.eventBuffer.length === 1) {
        await this.flushBuffer();
      }
      
      console.log('[MultiProjectorActor] Event processed successfully');
    } catch (error) {
      console.error('[MultiProjectorActor] Error processing pub/sub event:', error);
      throw error; // Let Dapr retry
    }
  }
}