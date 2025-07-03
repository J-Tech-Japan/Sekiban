import { AbstractActor, ActorId, DaprClient } from '@dapr/dapr';
import type { IEventStore, IQueryExecutor, EventDocument } from '@sekiban/core';
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
  private snapshotTimer?: NodeJS.Timer;
  private eventCheckTimer?: NodeJS.Timer;
  
  // Configuration
  private readonly SAFE_WINDOW_MS = 7000;        // 7 seconds
  private readonly SNAPSHOT_INTERVAL_MS = 300000; // 5 minutes
  private readonly EVENT_CHECK_INTERVAL_MS = 1000; // 1 second
  
  // State keys
  private readonly PROJECTION_STATE_KEY = "projectionState";
  private readonly PROCESSED_EVENTS_KEY = "processedEvents";
  
  constructor(
    daprClient: DaprClient,
    id: ActorId,
    private readonly eventStore: IEventStore,
    private readonly queryExecutor: IQueryExecutor,
    private readonly projectorType: string
  ) {
    super(daprClient, id);
  }
  
  /**
   * Actor activation - set up reminders/timers
   */
  async onActivate(): Promise<void> {
    try {
      // Try to register reminders
      await this.registerReminderAsync(
        this.SNAPSHOT_REMINDER,
        Buffer.from(""),
        "PT5M",  // 5 minutes
        "PT5M"
      );
      
      await this.registerReminderAsync(
        this.EVENT_CHECK_REMINDER,
        Buffer.from(""),
        "PT1S",  // 1 second
        "PT1S"
      );
    } catch (error) {
      // Fall back to timers if reminders fail
      console.warn('Failed to register reminders, falling back to timers:', error);
      
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
      
      // Execute query through query executor
      const result = await this.queryExecutor.executeQuery(
        query,
        state.projections
      );
      
      return {
        isSuccess: true,
        data: result
      };
    } catch (error) {
      return {
        isSuccess: false,
        error: error instanceof Error ? error.message : 'Unknown error'
      };
    }
  }
  
  /**
   * Execute list query
   */
  async queryListAsync(query: SerializableListQuery): Promise<ListQueryResponse> {
    try {
      await this.flushBuffer();
      
      // Use safe state for queries
      const state = this.safeState || await this.buildStateAsync();
      
      // Execute list query through query executor
      const result = await this.queryExecutor.executeListQuery(
        query,
        state.projections
      );
      
      return {
        isSuccess: true,
        items: result.items,
        totalCount: result.totalCount
      };
    } catch (error) {
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
   * Reminder handling
   */
  async receiveReminderAsync(
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
    const [hasState, state] = await (this as any).stateManager.tryGetState<MultiProjectionState>(
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
    await (this as any).stateManager.setState(
      this.PROJECTION_STATE_KEY,
      state
    );
  }
  
  /**
   * Get processed events set
   */
  private async getProcessedEventsAsync(): Promise<Set<string>> {
    const [hasEvents, events] = await (this as any).stateManager.tryGetState<string[]>(
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
      await (this as any).stateManager.setState(
        this.PROCESSED_EVENTS_KEY,
        trimmed
      );
    } else {
      await (this as any).stateManager.setState(
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