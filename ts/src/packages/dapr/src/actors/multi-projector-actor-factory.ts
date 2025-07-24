import type { SekibanDomainTypes } from '@sekiban/core';
import type { IActorProxyFactory } from '../types/index.js';
import { MultiProjectorActor } from './multi-projector-actor.js';
import { initializeDaprContainer } from '../container/index.js';

/**
 * Factory for creating and configuring MultiProjectorActor instances
 * This follows the same pattern as AggregateActorFactory
 */
export class MultiProjectorActorFactory {
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
    // Initialize the Awilix container with dependencies (same container as other actors)
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
  static createActorClass(): typeof MultiProjectorActor {
    // Return the actual class, not a wrapper
    return MultiProjectorActor;
  }
}