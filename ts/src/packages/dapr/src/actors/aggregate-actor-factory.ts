import type { SekibanDomainTypes } from '@sekiban/core';
import type { IActorProxyFactory } from '../types/index.js';
import { AggregateActor } from './aggregate-actor.js';
import { initializeDaprContainer } from '../container/index.js';

/**
 * Factory for creating and configuring AggregateActor instances
 * This is needed because DaprServer requires actors to have simple constructors
 */
export class AggregateActorFactory {
  /**
   * Configure the factory with dependencies
   */
  static configure(
    domainTypes: SekibanDomainTypes,
    serviceProvider: any,
    actorProxyFactory: IActorProxyFactory,
    serializationService: any,
    eventStore?: any,
    eventHandlerDirectCall?: (actorId: string, method: string, args: any[]) => Promise<any>
  ): void {
    // Initialize the Awilix container with dependencies
    initializeDaprContainer({
      domainTypes,
      serviceProvider,
      actorProxyFactory,
      serializationService,
      eventStore,
      eventHandlerDirectCall
    });
  }

  /**
   * Create an actor class that can be registered with DaprServer
   * Returns the actor class directly (no wrapper)
   */
  static createActorClass(): typeof AggregateActor {
    // Return the actual class, not a wrapper
    return AggregateActor;
  }
}