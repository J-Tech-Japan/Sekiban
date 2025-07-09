import type { IEventStore } from '@sekiban/core';
import { AggregateEventHandlerActor } from './aggregate-event-handler-actor.js';

/**
 * Factory for creating and configuring AggregateEventHandlerActor instances
 * This is needed because DaprServer requires actors to have simple constructors
 */
export class AggregateEventHandlerActorFactory {
  private static eventStore: IEventStore;

  /**
   * Configure the factory with dependencies
   */
  static configure(eventStore: IEventStore): void {
    this.eventStore = eventStore;
  }

  /**
   * Create an actor class that can be registered with DaprServer
   */
  static createActorClass(): typeof AggregateEventHandlerActor {
    const factory = this;
    
    return class extends AggregateEventHandlerActor {
      constructor(ctx: any, id: any) {
        super(ctx, id);
        // Inject dependencies after construction
        this.setupDependencies(factory.eventStore);
      }
    };
  }
}