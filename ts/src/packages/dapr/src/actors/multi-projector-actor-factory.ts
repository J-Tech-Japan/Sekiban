import { MultiProjectorActor } from './multi-projector-actor.js';
import { 
  SekibanDomainTypes, 
  IActorProxyFactory,
  IEventStore
} from '@sekiban/core';
import { initializeDaprContainer } from '../container/index.js';

/**
 * Factory for creating MultiProjectorActor
 */
export class MultiProjectorActorFactory {
  static configure(
    domainTypes: SekibanDomainTypes,
    serviceProvider: any,
    actorProxyFactory: IActorProxyFactory,
    serializationService: any,
    eventStore?: IEventStore,
    eventHandlerDirectCall?: (actorId: string, method: string, args: any[]) => Promise<any>
  ): void {
    console.log('[MultiProjectorActorFactory] Configuring with domainTypes:', !!domainTypes);
    console.log('[MultiProjectorActorFactory] Projector types available:', domainTypes?.projectorTypes ? 'yes' : 'no');
    
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
  
  static createActorClass(): typeof MultiProjectorActor {
    console.log('[MultiProjectorActorFactory] Creating actor class (returning MultiProjectorActor)');
    return MultiProjectorActor;
  }
}