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
    eventStore?: any
  ): void {
    console.log('[AggregateActorFactory] Configuring with domainTypes:', !!domainTypes);
    console.log('[AggregateActorFactory] Command types available:', domainTypes?.commandTypes ? domainTypes.commandTypes.getCommandTypes().map(c => c.name) : 'none');
    
    // Initialize the Awilix container with dependencies
    initializeDaprContainer({
      domainTypes,
      serviceProvider,
      actorProxyFactory,
      serializationService,
      eventStore
    });
  }

  /**
   * Create an actor class that can be registered with DaprServer
   * Now returns the wrapper class directly
   */
  static createActorClass(): typeof AggregateActor {
    console.log('[AggregateActorFactory] Creating actor class (returning AggregateActor)');
    return AggregateActor;
  }
}