import { Event, IEventPayload } from '../events/index.js';
import { PartitionKeys } from '../documents/index.js';

/**
 * Base interface for projections
 */
export interface IProjection {
  /**
   * The projection type identifier
   */
  projectionType: string;
  
  /**
   * The current version of the projection
   */
  version: number;
}

/**
 * Interface for multi-projections that aggregate data across multiple aggregates
 */
export interface IMultiProjection<TState> extends IProjection {
  /**
   * The projection state
   */
  state: TState;
  
  /**
   * Apply an event to update the projection
   */
  apply(event: Event): void;
  
  /**
   * Get the event types this projection is interested in
   */
  getInterestedEventTypes(): string[];
}

/**
 * Base class for multi-projections
 */
export abstract class MultiProjection<TState> implements IMultiProjection<TState> {
  public version = 0;
  
  constructor(
    public readonly projectionType: string,
    public state: TState
  ) {}

  /**
   * Registry of event handlers
   */
  private eventHandlers = new Map<string, (state: TState, event: Event) => TState>();

  /**
   * Registers an event handler
   */
  protected on<TPayload extends IEventPayload>(
    eventType: string | { new(...args: any[]): TPayload },
    handler: (state: TState, event: Event<TPayload>) => TState
  ): void {
    const type = typeof eventType === 'string' ? eventType : eventType.name;
    this.eventHandlers.set(type, handler as any);
  }

  /**
   * Applies an event to the projection
   */
  apply(event: Event): void {
    const handler = this.eventHandlers.get(event.payload.eventType);
    if (handler) {
      this.state = handler(this.state, event);
      this.version = event.version;
    }
  }

  /**
   * Gets the event types this projection handles
   */
  getInterestedEventTypes(): string[] {
    return Array.from(this.eventHandlers.keys());
  }

  /**
   * Abstract method to get initial state
   */
  abstract getInitialState(): TState;
}

/**
 * Projection builder for creating projections dynamically
 */
export class ProjectionBuilder<TState> {
  private handlers = new Map<string, (state: TState, event: Event) => TState>();
  
  constructor(
    private projectionType: string,
    private initialState: TState
  ) {}

  /**
   * Adds an event handler
   */
  on<TPayload extends IEventPayload>(
    eventType: string,
    handler: (state: TState, event: Event<TPayload>) => TState
  ): ProjectionBuilder<TState> {
    this.handlers.set(eventType, handler as any);
    return this;
  }

  /**
   * Builds the projection
   */
  build(): IMultiProjection<TState> {
    class DynamicProjection extends MultiProjection<TState> {
      constructor(handlers: Map<string, (state: TState, event: Event) => TState>) {
        super(this.projectionType, this.initialState);
        
        // Register all handlers
        handlers.forEach((handler, eventType) => {
          this.on(eventType, handler);
        });
      }

      getInitialState(): TState {
        return this.initialState;
      }
    }

    return new DynamicProjection(this.handlers);
  }
}

/**
 * Interface for projection stores
 */
export interface IProjectionStore {
  /**
   * Saves a projection
   */
  save<TState>(projection: IMultiProjection<TState>): Promise<void>;
  
  /**
   * Loads a projection
   */
  load<TState>(projectionType: string, projectionId: string): Promise<IMultiProjection<TState> | null>;
  
  /**
   * Queries projections
   */
  query<TState>(
    projectionType: string,
    filter: (state: TState) => boolean,
    limit?: number,
    offset?: number
  ): Promise<IMultiProjection<TState>[]>;
  
  /**
   * Deletes a projection
   */
  delete(projectionType: string, projectionId: string): Promise<void>;
}

/**
 * In-memory projection store implementation
 */
export class InMemoryProjectionStore implements IProjectionStore {
  private projections = new Map<string, Map<string, IMultiProjection<any>>>();

  async save<TState>(projection: IMultiProjection<TState>): Promise<void> {
    if (!this.projections.has(projection.projectionType)) {
      this.projections.set(projection.projectionType, new Map());
    }
    
    // Generate a unique ID for the projection
    const id = `${projection.projectionType}-${Date.now()}`;
    this.projections.get(projection.projectionType)!.set(id, projection);
  }

  async load<TState>(
    projectionType: string,
    projectionId: string
  ): Promise<IMultiProjection<TState> | null> {
    const typeProjections = this.projections.get(projectionType);
    return typeProjections?.get(projectionId) || null;
  }

  async query<TState>(
    projectionType: string,
    filter: (state: TState) => boolean,
    limit?: number,
    offset = 0
  ): Promise<IMultiProjection<TState>[]> {
    const typeProjections = this.projections.get(projectionType);
    if (!typeProjections) {
      return [];
    }

    const results = Array.from(typeProjections.values())
      .filter(p => filter(p.state))
      .slice(offset, limit ? offset + limit : undefined);

    return results;
  }

  async delete(projectionType: string, projectionId: string): Promise<void> {
    this.projections.get(projectionType)?.delete(projectionId);
  }
}

/**
 * Projection replayer for rebuilding projections from events
 */
export class ProjectionReplayer {
  constructor(
    private eventStore: import('../events').IEventStore,
    private projectionStore: IProjectionStore
  ) {}

  /**
   * Replays events to rebuild a projection
   */
  async replay<TState>(
    projection: IMultiProjection<TState>,
    fromVersion = 0
  ): Promise<void> {
    const interestedTypes = projection.getInterestedEventTypes();
    
    // Query events from the event store
    const eventsResult = await this.eventStore.queryEvents({
      eventTypes: interestedTypes,
      fromVersion,
    });

    if (eventsResult.isErr()) {
      throw eventsResult.error;
    }

    // Apply events to projection
    for (const event of eventsResult.value) {
      projection.apply(event);
    }

    // Save the updated projection
    await this.projectionStore.save(projection);
  }
}