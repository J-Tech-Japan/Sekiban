import { Result, ok, err } from 'neverthrow';
import { Aggregate, IAggregatePayload, IProjector } from './types';
import { Event, IEventPayload } from '../events';
import { EventApplicationError } from '../result';
import { PartitionKeys } from '../documents';

/**
 * Base class for aggregate projectors
 */
export abstract class AggregateProjector<TPayload extends IAggregatePayload> 
  implements IProjector<TPayload> {
  
  constructor(public readonly aggregateType: string) {}

  /**
   * Registry of event handlers
   */
  private eventHandlers = new Map<string, (aggregate: Aggregate<TPayload>, event: any) => Aggregate<TPayload>>();

  /**
   * Registers an event handler
   */
  protected on<TEvent extends IEventPayload>(
    eventType: string | { new(...args: any[]): TEvent },
    handler: (aggregate: Aggregate<TPayload>, event: TEvent) => Aggregate<TPayload>
  ): void {
    const type = typeof eventType === 'string' ? eventType : eventType.name;
    this.eventHandlers.set(type, handler);
  }

  /**
   * Applies an event to the aggregate
   */
  apply(aggregate: Aggregate<TPayload>, event: IEventPayload): Aggregate<TPayload> {
    const handler = this.eventHandlers.get(event.eventType);
    
    if (!handler) {
      // If no handler is registered, return the aggregate unchanged
      return aggregate;
    }

    if (this.canApply && !this.canApply(aggregate, event)) {
      throw new EventApplicationError(
        event.eventType,
        'Event cannot be applied to the current aggregate state'
      );
    }

    return handler(aggregate, event);
  }

  /**
   * Applies multiple events to the aggregate
   */
  applyEvents(aggregate: Aggregate<TPayload>, events: IEventPayload[]): Aggregate<TPayload> {
    return events.reduce((agg, event) => this.apply(agg, event), aggregate);
  }

  /**
   * Projects events to build the aggregate state
   */
  project(events: Event<IEventPayload>[], partitionKeys: PartitionKeys): Aggregate<TPayload> {
    let aggregate = this.getInitialState(partitionKeys);
    
    for (const event of events) {
      aggregate = this.apply(aggregate, event.payload);
      aggregate.version = event.version;
      aggregate.lastEventId = event.id;
    }
    
    return aggregate;
  }

  /**
   * Validates if an event can be applied (optional override)
   */
  canApply?(aggregate: Aggregate<TPayload>, event: IEventPayload): boolean;

  /**
   * Gets the initial state for the aggregate
   */
  abstract getInitialState(partitionKeys: PartitionKeys): Aggregate<TPayload>;
}

/**
 * Helper function to create a projector from event handlers
 */
export function createProjector<TPayload extends IAggregatePayload>(
  aggregateType: string,
  getInitialState: (partitionKeys: PartitionKeys) => Aggregate<TPayload>,
  handlers: Record<string, (aggregate: Aggregate<TPayload>, event: any) => Aggregate<TPayload>>
): IProjector<TPayload> {
  
  class DynamicProjector extends AggregateProjector<TPayload> {
    constructor() {
      super(aggregateType);
      
      // Register all handlers
      for (const [eventType, handler] of Object.entries(handlers)) {
        this.on(eventType, handler);
      }
    }

    getInitialState(partitionKeys: PartitionKeys): Aggregate<TPayload> {
      return getInitialState(partitionKeys);
    }
  }

  return new DynamicProjector();
}

/**
 * Combines multiple projectors into one
 */
export class CompositeProjector<TPayload extends IAggregatePayload> 
  implements IProjector<TPayload> {
  
  constructor(
    public readonly aggregateType: string,
    private projectors: IProjector<TPayload>[]
  ) {}

  apply(aggregate: Aggregate<TPayload>, event: IEventPayload): Aggregate<TPayload> {
    let result = aggregate;
    
    for (const projector of this.projectors) {
      if (projector.canApply && !projector.canApply(result, event)) {
        continue;
      }
      result = projector.apply(result, event);
    }
    
    return result;
  }

  getInitialState(partitionKeys: PartitionKeys): Aggregate<TPayload> {
    // Use the first projector's initial state
    return this.projectors[0].getInitialState(partitionKeys);
  }

  canApply(aggregate: Aggregate<TPayload>, event: IEventPayload): boolean {
    // Event can be applied if any projector can apply it
    return this.projectors.some(p => !p.canApply || p.canApply(aggregate, event));
  }
}