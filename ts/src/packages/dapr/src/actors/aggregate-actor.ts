import { AbstractActor, ActorId, DaprClient } from '@dapr/dapr';
import type { 
  IAggregatePayload, 
  IProjector, 
  EventDocument, 
  IEventStore,
  PartitionKeys
} from '@sekiban/core';
import { Result, ok, err } from 'neverthrow';
import type { AggregateSnapshot, SnapshotLoadResult } from '../snapshot/types';
import { SnapshotError, SnapshotErrorCode } from '../snapshot/types';
import type { ISnapshotStrategy } from '../snapshot/strategies';

/**
 * Base class for Dapr actors that manage aggregate state with snapshot support
 */
export abstract class DaprAggregateActor<TPayload extends IAggregatePayload> extends AbstractActor {
  private snapshot?: AggregateSnapshot<TPayload>;
  private currentState?: SnapshotLoadResult<TPayload>;
  
  constructor(
    daprClient: DaprClient,
    id: ActorId,
    private readonly eventStore: IEventStore,
    private readonly projector: IProjector<TPayload>,
    private readonly snapshotStrategy: ISnapshotStrategy
  ) {
    super(daprClient, id);
  }

  /**
   * Get the aggregate ID from the actor ID
   */
  protected get aggregateId(): string {
    return (this as any).id.toString();
  }

  /**
   * Called when the actor is activated
   */
  async onActivate(): Promise<void> {
    try {
      // Load snapshot from Dapr state
      const [hasSnapshot, snapshotData] = await (this as any).stateManager.tryGetState<AggregateSnapshot<TPayload>>('snapshot');
      if (snapshotData && this.isValidSnapshot(snapshotData)) {
        this.snapshot = snapshotData;
      }
    } catch (error) {
      // Log error but continue - we can rebuild from events
      console.error('Failed to load snapshot:', error);
      this.snapshot = undefined;
    }
  }

  /**
   * Get the current aggregate state
   */
  async getState(): Promise<Result<SnapshotLoadResult<TPayload>, SnapshotError>> {
    const startTime = Date.now();

    try {
      if (!this.snapshot) {
        // Rebuild entirely from events
        const result = await this.rebuildFromEvents();
        return result;
      }

      // Load new events since snapshot
      const newEventsResult = await this.loadEventsSince(this.snapshot.lastEventId);
      if (newEventsResult.isErr()) {
        return err(newEventsResult.error);
      }

      const newEvents = newEventsResult.value;
      if (newEvents.length === 0) {
        // Snapshot is up to date
        this.currentState = {
          payload: this.snapshot.payload,
          version: this.snapshot.version,
          fromSnapshot: true,
          eventsReplayed: 0,
          loadTimeMs: Date.now() - startTime,
        };
        return ok(this.currentState);
      }

      // Apply new events to snapshot
      const updatedState = this.applyEventsToSnapshot(this.snapshot, newEvents);
      this.currentState = {
        ...updatedState,
        fromSnapshot: true,
        eventsReplayed: newEvents.length,
        loadTimeMs: Date.now() - startTime,
      };

      return ok(this.currentState);
    } catch (error) {
      return err(new SnapshotError(
        `Failed to get aggregate state: ${error}`,
        SnapshotErrorCode.STORAGE_ERROR
      ));
    }
  }

  /**
   * Apply new events to the aggregate
   */
  async applyEvents(events: EventDocument[]): Promise<Result<void, SnapshotError>> {
    try {
      // Get current state
      const stateResult = await this.getState();
      if (stateResult.isErr()) {
        return err(stateResult.error);
      }

      const currentState = stateResult.value;
      
      // Apply events
      let newPayload = currentState.payload;
      let newVersion = currentState.version;
      
      for (const event of events) {
        newPayload = this.projector.applyEvent(newPayload, event);
        newVersion = event.version;
      }

      // Update current state
      this.currentState = {
        payload: newPayload,
        version: newVersion,
        fromSnapshot: currentState.fromSnapshot,
        eventsReplayed: currentState.eventsReplayed + events.length,
        loadTimeMs: currentState.loadTimeMs,
      };

      // Check if we should take a snapshot
      const lastSnapshotVersion = this.snapshot?.version || 0;
      const lastSnapshotTime = this.snapshot?.snapshotTimestamp || null;
      
      if (this.snapshotStrategy.shouldTakeSnapshot(
        newVersion,
        lastSnapshotVersion,
        lastSnapshotTime
      )) {
        const snapshotResult = await this.createSnapshot();
        if (snapshotResult.isErr()) {
          // Log but don't fail - snapshot is optimization
          console.error('Failed to create snapshot:', snapshotResult.error);
        }
      }

      return ok(undefined);
    } catch (error) {
      return err(new SnapshotError(
        `Failed to apply events: ${error}`,
        SnapshotErrorCode.STORAGE_ERROR
      ));
    }
  }

  /**
   * Force creation of a snapshot
   */
  async createSnapshot(): Promise<Result<void, SnapshotError>> {
    try {
      const stateResult = await this.getState();
      if (stateResult.isErr()) {
        return err(stateResult.error);
      }

      const currentState = stateResult.value;
      
      // Get the last event to determine snapshot position
      const eventsResult = await this.loadEventsSince(null);
      if (eventsResult.isErr()) {
        return err(eventsResult.error);
      }

      const events = eventsResult.value;
      if (events.length === 0) {
        return ok(undefined); // No events to snapshot
      }

      const lastEvent = events[events.length - 1];
      
      this.snapshot = {
        aggregateId: this.aggregateId,
        partitionKey: lastEvent.partitionKeys,
        payload: currentState.payload,
        version: currentState.version,
        lastEventId: lastEvent.id,
        lastEventTimestamp: lastEvent.createdAt,
        snapshotTimestamp: new Date(),
      };

      // Save to Dapr state
      await (this as any).stateManager.setState('snapshot', this.snapshot);
      
      return ok(undefined);
    } catch (error) {
      return err(new SnapshotError(
        `Failed to save snapshot: ${error}`,
        SnapshotErrorCode.STORAGE_ERROR
      ));
    }
  }

  /**
   * Rebuild aggregate state entirely from events
   */
  private async rebuildFromEvents(): Promise<Result<SnapshotLoadResult<TPayload>, SnapshotError>> {
    const startTime = Date.now();
    
    try {
      const eventsResult = await this.loadEventsSince(null);
      if (eventsResult.isErr()) {
        return err(eventsResult.error);
      }

      const events = eventsResult.value;
      let payload = this.projector.initialState();
      let version = 0;

      for (const event of events) {
        payload = this.projector.applyEvent(payload, event);
        version = event.version;
      }

      return ok({
        payload,
        version,
        fromSnapshot: false,
        eventsReplayed: events.length,
        loadTimeMs: Date.now() - startTime,
      });
    } catch (error) {
      return err(new SnapshotError(
        `Failed to rebuild from events: ${error}`,
        SnapshotErrorCode.STORAGE_ERROR
      ));
    }
  }

  /**
   * Load events after a specific event ID
   */
  private async loadEventsSince(
    afterEventId: string | null
  ): Promise<Result<EventDocument[], SnapshotError>> {
    try {
      const events = await this.eventStore.loadEventsSince(
        this.aggregateId,
        afterEventId
      );
      return ok(events);
    } catch (error) {
      return err(new SnapshotError(
        `Failed to load events: ${error}`,
        SnapshotErrorCode.STORAGE_ERROR
      ));
    }
  }

  /**
   * Apply events to an existing snapshot
   */
  private applyEventsToSnapshot(
    snapshot: AggregateSnapshot<TPayload>,
    events: EventDocument[]
  ): SnapshotLoadResult<TPayload> {
    let payload = snapshot.payload;
    let version = snapshot.version;

    for (const event of events) {
      payload = this.projector.applyEvent(payload, event);
      version = event.version;
    }

    return {
      payload,
      version,
      fromSnapshot: true,
      eventsReplayed: events.length,
      loadTimeMs: 0, // Will be set by caller
    };
  }

  /**
   * Validate snapshot data structure
   */
  private isValidSnapshot(data: any): data is AggregateSnapshot<TPayload> {
    return (
      data &&
      typeof data.aggregateId === 'string' &&
      data.partitionKey &&
      data.payload &&
      typeof data.version === 'number' &&
      typeof data.lastEventId === 'string' &&
      data.lastEventTimestamp &&
      data.snapshotTimestamp
    );
  }
}

/**
 * Extension to IEventStore for actor-specific needs
 */
export interface IActorEventStore extends IEventStore {
  /**
   * Load events for a specific aggregate after a given event ID
   */
  loadEventsSince(
    aggregateId: string,
    afterEventId: string | null
  ): Promise<EventDocument[]>;
}