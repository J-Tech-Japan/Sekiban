import { AbstractActor, ActorId, DaprClient } from '@dapr/dapr';
import type { IEventStore, EventDocument } from '@sekiban/core';
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
        loadAllEvents: async () => [],
        loadEventsAfter: async () => [],
        saveEvents: async () => {},
        loadEvents: async () => []
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
    const events = await this.eventStore.loadAllEvents();
    
    // Build new state
    const newState = this.createEmptyState();
    
    // Apply all events
    for (const event of events) {
      newState.projections = await this.applyEventToProjections(
        newState.projections,
        this.serializeEvent(event)
      );
      newState.lastProcessedEventId = event.sortableUniqueId;
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
      // Load events after last processed
      const newEvents = await this.eventStore.loadEventsAfter(
        lastProcessedId,
        1000 // Batch size
      );
      
      // Add to buffer if not already present
      for (const event of newEvents) {
        const serialized = this.serializeEvent(event);
        
        if (!await this.isSortableUniqueIdReceived(serialized.sortableUniqueId)) {
          this.eventBuffer.push({
            event: serialized,
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
    // This would call the actual projector logic
    // For now, return unchanged projections
    // TODO: Implement actual projection logic based on projector type
    return projections;
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
  private serializeEvent(event: EventDocument): SerializableEventDocument {
    return {
      id: event.id,
      sortableUniqueId: event.sortableUniqueId,
      payload: event.payload,
      eventType: event.payload?.constructor?.name || 'Unknown',
      aggregateId: event.aggregateId,
      partitionKeys: event.partitionKeys,
      version: event.version,
      createdAt: event.createdAt.toISOString(),
      metadata: event.metadata || {}
    };
  }
}