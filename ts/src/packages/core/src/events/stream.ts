import { Event, EventFilter, EventHandler, EventSubscription } from './types.js';
import { IEventPayload } from './event-payload.js';

/**
 * Interface for event stream implementations
 */
export interface IEventStream {
  /**
   * Subscribes to all events
   */
  subscribeToAll(handler: EventHandler): Promise<EventSubscription>;
  
  /**
   * Subscribes to events matching a filter
   */
  subscribe(filter: EventFilter, handler: EventHandler): Promise<EventSubscription>;
  
  /**
   * Subscribes to events of specific types
   */
  subscribeToTypes<TPayload extends IEventPayload = IEventPayload>(
    eventTypes: string[],
    handler: EventHandler<TPayload>
  ): Promise<EventSubscription>;
  
  /**
   * Subscribes to events for a specific aggregate
   */
  subscribeToAggregate(
    aggregateType: string,
    aggregateId: string,
    handler: EventHandler
  ): Promise<EventSubscription>;
  
  /**
   * Publishes an event to the stream
   */
  publish(event: Event): Promise<void>;
  
  /**
   * Publishes multiple events to the stream
   */
  publishMany(events: Event[]): Promise<void>;
}

/**
 * In-memory implementation of event stream
 */
export class InMemoryEventStream implements IEventStream {
  private handlers: Map<string, Set<EventHandler>> = new Map();
  private nextSubscriptionId = 1;

  async subscribeToAll(handler: EventHandler): Promise<EventSubscription> {
    const id = `sub-${this.nextSubscriptionId++}`;
    
    if (!this.handlers.has('*')) {
      this.handlers.set('*', new Set());
    }
    this.handlers.get('*')!.add(handler);
    
    return {
      id,
      unsubscribe: async () => {
        this.handlers.get('*')?.delete(handler);
      },
    };
  }

  async subscribe(filter: EventFilter, handler: EventHandler): Promise<EventSubscription> {
    const id = `sub-${this.nextSubscriptionId++}`;
    const filterKey = JSON.stringify(filter);
    
    if (!this.handlers.has(filterKey)) {
      this.handlers.set(filterKey, new Set());
    }
    
    const wrappedHandler: EventHandler = async (event) => {
      if (this.matchesFilter(event, filter)) {
        await handler(event);
      }
    };
    
    this.handlers.get(filterKey)!.add(wrappedHandler);
    
    return {
      id,
      unsubscribe: async () => {
        this.handlers.get(filterKey)?.delete(wrappedHandler);
      },
    };
  }

  async subscribeToTypes<TPayload extends IEventPayload = IEventPayload>(
    eventTypes: string[],
    handler: EventHandler<TPayload>
  ): Promise<EventSubscription> {
    return this.subscribe({ eventTypes }, handler as EventHandler);
  }

  async subscribeToAggregate(
    aggregateType: string,
    aggregateId: string,
    handler: EventHandler
  ): Promise<EventSubscription> {
    return this.subscribe({ aggregateType, aggregateId }, handler);
  }

  async publish(event: Event): Promise<void> {
    // Notify all handlers
    const allHandlers = this.handlers.get('*');
    if (allHandlers) {
      await Promise.all(Array.from(allHandlers).map(h => h(event)));
    }
    
    // Notify filtered handlers
    for (const [filterKey, handlers] of this.handlers.entries()) {
      if (filterKey === '*') continue;
      
      await Promise.all(Array.from(handlers).map(h => h(event)));
    }
  }

  async publishMany(events: Event[]): Promise<void> {
    for (const event of events) {
      await this.publish(event);
    }
  }

  private matchesFilter(event: Event, filter: EventFilter): boolean {
    if (filter.aggregateId && event.partitionKeys.aggregateId !== filter.aggregateId) {
      return false;
    }
    
    if (filter.aggregateType && event.aggregateType !== filter.aggregateType) {
      return false;
    }
    
    if (filter.eventTypes && !filter.eventTypes.includes(event.eventType)) {
      return false;
    }
    
    if (filter.fromVersion !== undefined && event.version < filter.fromVersion) {
      return false;
    }
    
    if (filter.toVersion !== undefined && event.version > filter.toVersion) {
      return false;
    }
    
    if (filter.fromTimestamp && event.metadata.timestamp < filter.fromTimestamp) {
      return false;
    }
    
    if (filter.toTimestamp && event.metadata.timestamp > filter.toTimestamp) {
      return false;
    }
    
    return true;
  }
}