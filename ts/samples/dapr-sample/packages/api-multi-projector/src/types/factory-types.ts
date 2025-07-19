/**
 * Type definitions for actor factory patterns
 */

import type { MultiProjectorActor } from '@sekiban/dapr';

/**
 * Type for MultiProjectorActorFactory configure method
 */
export interface MultiProjectorActorFactoryConfigureMethod {
  configure(
    domainTypes: any,
    serviceProvider: any,
    actorProxyFactory: any,
    serializationService: any,
    eventStore: any
  ): void;
}

/**
 * Type for MultiProjectorActorFactory createActorClass method
 */
export interface MultiProjectorActorFactoryCreateMethod {
  createActorClass(): typeof MultiProjectorActor;
}

/**
 * Combined type for MultiProjectorActorFactory with both methods
 */
export interface TypedMultiProjectorActorFactory 
  extends MultiProjectorActorFactoryConfigureMethod, 
          MultiProjectorActorFactoryCreateMethod {}