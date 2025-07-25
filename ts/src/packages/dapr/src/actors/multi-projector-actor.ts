import { AbstractActor, ActorId, DaprClient } from '@dapr/dapr';
import { Temporal } from '@js-temporal/polyfill';
import type { IEventStore, IEvent, SekibanDomainTypes, IMultiProjectorCommon, IMultiProjectorStateCommon, IQueryContext, IListQueryResult, IQueryResult } from '@sekiban/core';
import {
  EventRetrievalInfo,
  SortableIdCondition,
  SortableUniqueId,
  ok,
  err,
  Result
} from '@sekiban/core';
import type {
  IMultiProjectorActor,
  SerializableQuery,
  SerializableListQuery,
  QueryResponse,
  ListQueryResponse,
  DaprEventEnvelope
} from './interfaces';
import { getDaprCradle } from '../container/index.js';
import { 
  SerializableMultiProjectionState, 
  MultiProjectionState,
  createSerializableMultiProjectionState,
  toMultiProjectionState
} from '../parts/serializable-multi-projection-state.js';

/**
 * Handles cross-aggregate projections and queries over multiple aggregates
 * Mirrors C# MultiProjectorActor implementation exactly
 */
export class MultiProjectorActor extends AbstractActor implements IMultiProjectorActor {
  // Explicitly define actor type for Dapr
  static get actorType() { 
    return "MultiProjectorActor"; 
  }

  // ---------- Tunables ----------
  private readonly SAFE_STATE_WINDOW_MS = 7000; // 7 seconds
  private readonly PERSIST_INTERVAL_MS = 300000; // 5 minutes
  
  // ---------- State ----------
  private safeState?: MultiProjectionState;
  private unsafeState?: MultiProjectionState;
  
  // ---------- Infra ----------
  private eventStore: IEventStore;
  private domainTypes: SekibanDomainTypes;
  private actorIdString: string = '';
  
  // ---------- Event Buffer ----------
  private buffer: IEvent[] = [];
  private bootstrapping = true;
  private pendingSave = false;
  private eventDeliveryActive = false;
  private lastEventReceivedTime = new Date(0);
  
  // ---------- State Keys ----------
  private readonly STATE_KEY = "multiprojector_state";
  private readonly SNAPSHOT_REMINDER_NAME = "snapshot_reminder";
  
  constructor(daprClient: DaprClient, id: ActorId) {
    super(daprClient, id);
    
    // Extract actor ID string
    this.actorIdString = (id as any).id || String(id);
    
    // Get dependencies from Awilix container
    const cradle = getDaprCradle();
    this.eventStore = cradle.eventStore;
    this.domainTypes = cradle.domainTypes;
    
    console.log('[MultiProjectorActor] Initialized for projector:', this.actorIdString);
  }
  
  async onActivate(): Promise<void> {
    await super.onActivate();
    
    console.log(`MultiProjectorActor ${this.actorIdString} activated`);
    
    try {
      // Register reminder for periodic snapshot saving
      await this.registerReminder(
        this.SNAPSHOT_REMINDER_NAME,
        Temporal.Duration.from({ minutes: 1 }), // Initial delay
        Temporal.Duration.from({ minutes: 5 })  // Period
      );
      
      console.log('Snapshot reminder registered successfully');
    } catch (error) {
      console.warn('Failed to register reminder, falling back to timer:', error);
      
      // Fallback to timer if reminder fails
      await this.registerActorTimer(
        'SnapshotTimer',
        this.handleSnapshotTimerAsync.bind(this),
        null,
        Temporal.Duration.from({ minutes: 1 }),
        Temporal.Duration.from({ minutes: 5 })
      );
    }
    
    // Initial state loading and catch-up will be done via ensureStateLoadedAsync
    // when the first event arrives via PubSub
    this.bootstrapping = false;
    
    // Mark event delivery as active initially to allow batch processing during startup
    this.eventDeliveryActive = true;
    this.lastEventReceivedTime = new Date();
  }
  
  async onDeactivate(): Promise<void> {
    console.log(`MultiProjectorActor ${this.actorIdString} deactivating`);
    
    // Save pending state if any
    if (this.pendingSave && this.safeState) {
      await this.persistStateAsync(this.safeState);
    }
    
    // Unregister snapshot reminder
    await this.unregisterReminder(this.SNAPSHOT_REMINDER_NAME);
    
    await super.onDeactivate();
  }
  
  async receiveReminder(reminderName: string): Promise<void> {
    switch (reminderName) {
      case this.SNAPSHOT_REMINDER_NAME:
        await this.handleSnapshotReminder();
        break;
      default:
        console.warn('Unknown reminder:', reminderName);
        break;
    }
  }
  
  private async handleSnapshotReminder(): Promise<void> {
    // First, flush any buffered events to ensure state is up to date
    if (this.buffer.length > 0) {
      console.log(`Snapshot reminder triggered - flushing ${this.buffer.length} buffered events`);
      this.flushBuffer();
    }
    
    // Then persist if needed
    if (!this.pendingSave || !this.safeState) return;
    
    this.pendingSave = false;
    await this.persistStateAsync(this.safeState);
  }
  
  /**
   * Timer fallback for snapshot handling when reminders are not available
   */
  private async handleSnapshotTimerAsync(): Promise<void> {
    await this.handleSnapshotReminder();
  }
  
  private async ensureStateLoadedAsync(): Promise<void> {
    if (this.safeState) return;
    
    console.log(`Loading snapshot for MultiProjectorActor ${this.actorIdString}`);
    
    try {
      const savedState = await this.getStateManager().tryGetState<SerializableMultiProjectionState>(this.STATE_KEY);
      if (savedState) {
        const restored = await this.deserializeState(savedState);
        if (restored) {
          this.safeState = restored;
          this.logState();
        }
      }
    } catch (error) {
      console.error('Failed to load state:', error);
    }
    
    // Initialize state if not loaded
    if (!this.safeState) {
      this.initializeState();
    }
    
    // Catch up from store
    await this.catchUpFromStoreAsync();
  }
  
  private logState(): void {
    console.log('SafeState:', this.safeState?.projectorCommon.constructor.name ?? 'null');
    console.log('SafeState Version:', this.safeState?.version ?? 0);
    
    if (this.safeState?.projectorCommon && 'getAggregates' in this.safeState.projectorCommon) {
      const accessor = this.safeState.projectorCommon as any;
      console.log('SafeState list count:', accessor.getAggregates().length);
    }
    
    console.log('UnsafeState:', this.unsafeState?.projectorCommon.constructor.name ?? 'null');
    console.log('UnsafeState Version:', this.unsafeState?.version ?? 0);
    
    if (this.unsafeState?.projectorCommon && 'getAggregates' in this.unsafeState.projectorCommon) {
      const accessor = this.unsafeState.projectorCommon as any;
      console.log('UnsafeState list count:', accessor.getAggregates().length);
    }
  }
  
  private async catchUpFromStoreAsync(): Promise<void> {
    const lastId = this.safeState?.lastSortableUniqueId || '';
    const retrieval: EventRetrievalInfo = {
      streams: [],
      sortableIdCondition: lastId
        ? SortableIdCondition.between(
            SortableUniqueId.fromString(lastId),
            SortableUniqueId.generate()
          )
        : SortableIdCondition.none(),
      limit: undefined,
      includeMetadata: false
    };
    
    const eventsResult = await this.eventStore.getEvents(retrieval);
    if (eventsResult.isErr()) return;
    
    const events = eventsResult.value;
    console.log(`Catch Up Starting Events ${events.length} events`);
    
    if (events.length > 0) {
      // Add events to buffer with duplicate check
      for (const e of events) {
        if (!this.buffer.some(existingEvent => existingEvent.sortableUniqueId.value === e.sortableUniqueId.value)) {
          this.buffer.push(e);
        } else {
          console.log(`Skipping duplicate event during catch-up: ${e.sortableUniqueId.value}`);
        }
      }
      this.flushBuffer();
    }
    
    console.log(`Catch Up Finished ${events.length} events`);
    this.logState();
  }
  
  private flushBuffer(): void {
    console.log(`Start flush buffer ${this.buffer.length} events`);
    this.logState();
    
    if (!this.safeState && !this.unsafeState) {
      this.initializeState();
    }
    
    if (this.buffer.length === 0) {
      // Even with no events, ensure state consistency
      this.unsafeState = undefined; // No unsafe state when buffer is empty
      return;
    }
    
    const projector = this.getProjectorFromName();
    if (!projector) {
      console.error('Failed to get projector from name');
      return;
    }
    
    // Sort buffer by sortable unique ID
    this.buffer.sort((a, b) => {
      return a.sortableUniqueId.compareTo(b.sortableUniqueId);
    });
    
    // Calculate safe border (7 seconds ago)
    const safeBorder = SortableUniqueId.fromDate(new Date(Date.now() - this.SAFE_STATE_WINDOW_MS));
    
    // Find split index
    const splitIndex = this.buffer.findLastIndex(e => 
      e.sortableUniqueId.compareTo(safeBorder) < 0
    );
    
    console.log(`Splitted Total ${this.buffer.length} events, SplitIndex ${splitIndex}`);
    
    // Process old events
    if (splitIndex >= 0) {
      console.log('Working on old events');
      const sortableUniqueIdFrom = this.safeState?.lastSortableUniqueId 
        ? SortableUniqueId.fromString(this.safeState.lastSortableUniqueId)
        : SortableUniqueId.min();
      
      // Get old events
      const oldEvents = this.buffer.slice(0, splitIndex + 1)
        .filter(e => e.sortableUniqueId.compareTo(sortableUniqueIdFrom) > 0);
      
      if (oldEvents.length > 0 && this.domainTypes.multiProjectorTypes) {
        // Apply to safe state
        const currentProjector = this.safeState?.projectorCommon ?? projector;
        const newSafeStateResult = this.domainTypes.multiProjectorTypes.projectEvents(currentProjector, oldEvents);
        
        if (newSafeStateResult.isOk()) {
          const lastOldEvt = oldEvents[oldEvents.length - 1];
          this.safeState = {
            projectorCommon: newSafeStateResult.value,
            lastEventId: lastOldEvt.id.value,
            lastSortableUniqueId: lastOldEvt.sortableUniqueId.value,
            version: (this.safeState?.version ?? 0) + 1,
            appliedSnapshotVersion: 0,
            rootPartitionKey: this.safeState?.rootPartitionKey ?? 'default'
          };
          
          // Mark for saving
          this.pendingSave = true;
        }
      }
      
      // Remove processed events from buffer
      this.buffer.splice(0, splitIndex + 1);
    }
    
    console.log(`After worked old events Total ${this.buffer.length} events`);
    
    // Process remaining (newer) events for unsafe state
    if (this.buffer.length > 0 && this.safeState && this.domainTypes.multiProjectorTypes) {
      const newUnsafeStateResult = this.domainTypes.multiProjectorTypes.projectEvents(
        this.safeState.projectorCommon, 
        this.buffer
      );
      
      if (newUnsafeStateResult.isOk()) {
        const lastNewEvt = this.buffer[this.buffer.length - 1];
        this.unsafeState = {
          projectorCommon: newUnsafeStateResult.value,
          lastEventId: lastNewEvt.id.value,
          lastSortableUniqueId: lastNewEvt.sortableUniqueId.value,
          version: this.safeState.version + 1,
          appliedSnapshotVersion: 0,
          rootPartitionKey: this.safeState.rootPartitionKey
        };
      }
    } else {
      this.unsafeState = undefined;
    }
    
    console.log(`Finish flush buffer ${this.buffer.length} events`);
    this.logState();
  }
  
  private initializeState(): void {
    const projector = this.getProjectorFromName();
    if (!projector) {
      console.error('Failed to get projector from name');
      return;
    }
    
    this.safeState = {
      projectorCommon: projector,
      lastEventId: '',
      lastSortableUniqueId: '',
      version: 0,
      appliedSnapshotVersion: 0,
      rootPartitionKey: 'default'
    };
  }
  
  private async persistStateAsync(state: MultiProjectionState): Promise<void> {
    if (state.version === 0) return;
    
    console.log(`Persisting state version ${state.version}`);
    
    const serializableState = await this.serializeState(state);
    await this.getStateManager().setState(this.STATE_KEY, serializableState);
    
    console.log(`Persisting state written version ${state.version}`);
  }
  
  async query(query: SerializableQuery): Promise<QueryResponse> {
    try {
      await this.ensureStateLoadedAsync();
      
      // For now, return empty result
      // TODO: Implement query deserialization and execution
      return {
        value: null,
        hasError: false,
        error: null
      };
    } catch (error) {
      console.error('Error executing query:', error);
      return {
        value: null,
        hasError: true,
        error: error instanceof Error ? error.message : 'Unknown error'
      };
    }
  }
  
  async queryList(query: SerializableListQuery): Promise<ListQueryResponse> {
    try {
      await this.ensureStateLoadedAsync();
      
      // Get the appropriate state
      const projectionState = await this.getProjectorForQuery();
      if (!projectionState) {
        throw new Error('Failed to get projector state');
      }
      const state = projectionState.projectorCommon;
      
      // Execute the query using domain types
      if (!this.domainTypes.queryTypes) {
        throw new Error('Query types not available');
      }
      
      // Get the query class by name
      const QueryClass = this.domainTypes.queryTypes.getQueryTypeByName(query.queryType);
      if (!QueryClass) {
        throw new Error(`Query type not found: ${query.queryType}`);
      }
      
      // Create query instance
      const queryInstance = new QueryClass();
      
      // If the query has filter/sort data, apply it
      if (query.payload) {
        Object.assign(queryInstance, query.payload);
      }
      
      // Apply pagination
      if (query.skip !== undefined) queryInstance.skip = query.skip;
      if (query.take !== undefined) queryInstance.take = query.take;
      if (query.limit !== undefined) queryInstance.limit = query.limit;
      
      // For now, we'll manually execute the query against the state
      // This is a simplified implementation until full query infrastructure is in place
      if (state && 'getAggregates' in state) {
        const accessor = state as any;
        let aggregates = Array.from(accessor.getAggregates());
        
        // Apply filter if query has handleFilter method
        if (queryInstance.handleFilter) {
          aggregates = aggregates.filter(([key, aggregate]) => 
            queryInstance.handleFilter(aggregate)
          );
        }
        
        // Apply sort if query has handleSort method
        if (queryInstance.handleSort) {
          aggregates.sort((a, b) => queryInstance.handleSort(a[1], b[1]));
        }
        
        // Apply pagination
        const skip = query.skip || 0;
        const take = query.take || query.limit || 100;
        const paginatedAggregates = aggregates.slice(skip, skip + take);
        
        // Return the aggregates (just the values, not the keys)
        return {
          values: paginatedAggregates.map(([_, aggregate]) => aggregate),
          hasError: false,
          error: null
        };
      }
      
      // Fallback for non-list projectors
      return {
        values: [],
        hasError: false,
        error: null
      };
    } catch (error) {
      console.error('Error executing list query:', error);
      return {
        values: [],
        hasError: true,
        error: error instanceof Error ? error.message : 'Unknown error'
      };
    }
  }
  
  async isSortableUniqueIdReceived(sortableUniqueId: string): Promise<boolean> {
    await this.ensureStateLoadedAsync();
    
    // Check if exactly this event is in buffer
    if (this.buffer.some(e => e.sortableUniqueId.value === sortableUniqueId)) {
      return true;
    }
    
    // Check if already processed in safe state
    if (this.safeState?.lastSortableUniqueId) {
      const lastId = SortableUniqueId.fromString(this.safeState.lastSortableUniqueId);
      const targetId = SortableUniqueId.fromString(sortableUniqueId);
      
      if (lastId.compareTo(targetId) >= 0) {
        return true;
      }
    }
    
    // Check if already processed in unsafe state
    if (this.unsafeState?.lastSortableUniqueId) {
      const lastId = SortableUniqueId.fromString(this.unsafeState.lastSortableUniqueId);
      const targetId = SortableUniqueId.fromString(sortableUniqueId);
      
      if (lastId.compareTo(targetId) >= 0) {
        return true;
      }
    }
    
    return false;
  }
  
  async buildState(): Promise<void> {
    await this.ensureStateLoadedAsync();
    
    // Check if we're actively receiving events
    const eventDeliveryTimeout = 30000; // 30 seconds
    const isActivelyReceivingEvents = this.eventDeliveryActive && 
      (Date.now() - this.lastEventReceivedTime.getTime()) < eventDeliveryTimeout;
    
    if (isActivelyReceivingEvents) {
      console.log(`Event delivery active, flush buffer ${this.buffer.length} events`);
      this.flushBuffer();
      console.log(`Event delivery active, flush buffer finished ${this.buffer.length} events`);
    } else {
      console.log('Event delivery inactive, catch up from store');
      await this.catchUpFromStoreAsync();
      console.log('Event delivery inactive, catch up from store finished');
    }
  }
  
  async rebuildState(): Promise<void> {
    this.safeState = undefined;
    this.unsafeState = undefined;
    this.buffer = [];
    await this.catchUpFromStoreAsync();
    this.pendingSave = true;
  }
  
  /**
   * Handles events published through Dapr PubSub.
   */
  async handlePublishedEvent(envelope: DaprEventEnvelope): Promise<void> {
    try {
      console.log(`Received event from PubSub: EventId=${envelope.eventId}, AggregateId=${envelope.aggregateId}, Version=${envelope.version}`);
      
      // Mark event delivery as active
      this.eventDeliveryActive = true;
      this.lastEventReceivedTime = new Date();
      
      // Ensure state is loaded before processing
      await this.ensureStateLoadedAsync();
      
      // Deserialize the event
      const event = await this.deserializeEvent(envelope);
      if (!event) {
        console.warn('Failed to deserialize event from envelope');
        return;
      }
      
      // First check if this exact event is already in the buffer
      if (this.buffer.some(e => e.sortableUniqueId.value === event.sortableUniqueId.value)) {
        console.log(`Event already in buffer: ${event.sortableUniqueId.value}`);
        return;
      }
      
      // Then check if we've already processed this event in state
      if (this.safeState?.lastSortableUniqueId) {
        const lastSafeId = SortableUniqueId.fromString(this.safeState.lastSortableUniqueId);
        const eventId = event.sortableUniqueId;
        
        if (lastSafeId.compareTo(eventId) >= 0) {
          console.log(`Event already processed in safe state: ${event.sortableUniqueId.value}`);
          return;
        }
      }
      
      // Add to buffer
      this.buffer.push(event);
      console.log(`Added event to buffer: ${event.sortableUniqueId.value}`);
      
      // Similar to C#, we don't flush immediately during bootstrapping
      if (this.bootstrapping) {
        console.log('Bootstrapping mode - deferring flush');
      } else {
        // Buffer will be flushed when queries are made or during periodic snapshots
        console.log('Event buffered for batch processing');
      }
    } catch (error) {
      console.error('Error handling published event:', error);
    }
  }
  
  private getProjectorFromName(): IMultiProjectorCommon | undefined {
    if (!this.domainTypes.multiProjectorTypes) {
      console.error('MultiProjectorTypes not available');
      return undefined;
    }
    
    return this.domainTypes.multiProjectorTypes.getProjectorFromMultiProjectorName(this.actorIdString);
  }
  
  private async getProjectorForQuery(): Promise<MultiProjectionState | null> {
    await this.ensureStateLoadedAsync();
    
    // Check if we're actively receiving events
    const eventDeliveryTimeout = 30000; // 30 seconds
    const isActivelyReceivingEvents = this.eventDeliveryActive && 
      (Date.now() - this.lastEventReceivedTime.getTime()) < eventDeliveryTimeout;
    
    if (isActivelyReceivingEvents) {
      // If actively receiving events, flush buffer to ensure consistency
      console.log(`Event delivery active, flush buffer ${this.buffer.length} events`);
      this.flushBuffer();
      console.log(`Event delivery active, flush buffer finished ${this.buffer.length} events`);
    } else {
      // If not actively receiving events, catch up from store
      console.log('Event delivery inactive, catch up from store');
      await this.catchUpFromStoreAsync();
      console.log('Event delivery inactive, catch up from store finished');
    }
    
    const state = this.unsafeState ?? this.safeState;
    if (!state) {
      // Initialize state if not available
      this.initializeState();
      return this.safeState ?? null;
    }
    
    return state;
  }
  
  private async serializeState(state: MultiProjectionState): Promise<SerializableMultiProjectionState> {
    return createSerializableMultiProjectionState(state, this.domainTypes);
  }
  
  private async deserializeState(serialized: SerializableMultiProjectionState): Promise<MultiProjectionState | null> {
    return toMultiProjectionState(serialized, this.domainTypes);
  }
  
  private async deserializeEvent(envelope: DaprEventEnvelope): Promise<IEvent | null> {
    try {
      const serializedEvent = envelope.event;
      
      // Convert SerializableEventDocument to IEvent
      const event: IEvent = {
        id: { value: serializedEvent.id },
        sortableUniqueId: SortableUniqueId.fromString(serializedEvent.sortableUniqueId),
        aggregateId: { value: serializedEvent.aggregateId },
        partitionKeys: serializedEvent.partitionKeys,
        version: serializedEvent.version,
        createdAt: new Date(serializedEvent.createdAt),
        eventType: serializedEvent.eventType,
        payload: serializedEvent.payload,
        metadata: serializedEvent.metadata || {}
      };
      
      return event;
    } catch (error) {
      console.error('Failed to deserialize event:', error);
      return null;
    }
  }
}