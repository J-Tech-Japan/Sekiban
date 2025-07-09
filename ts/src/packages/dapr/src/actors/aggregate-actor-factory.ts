import type { SekibanDomainTypes } from '@sekiban/core';
import type { IActorProxyFactory } from '../types/index.js';
import { AggregateActor } from './aggregate-actor.js';

/**
 * Factory for creating and configuring AggregateActor instances
 * This is needed because DaprServer requires actors to have simple constructors
 */
export class AggregateActorFactory {
  private static domainTypes: SekibanDomainTypes;
  private static serviceProvider: any;
  private static actorProxyFactory: IActorProxyFactory;
  private static serializationService: any;

  /**
   * Configure the factory with dependencies
   */
  static configure(
    domainTypes: SekibanDomainTypes,
    serviceProvider: any,
    actorProxyFactory: IActorProxyFactory,
    serializationService: any
  ): void {
    this.domainTypes = domainTypes;
    this.serviceProvider = serviceProvider;
    this.actorProxyFactory = actorProxyFactory;
    this.serializationService = serializationService;
  }

  /**
   * Create an actor class that can be registered with DaprServer
   */
  static createActorClass(): typeof AggregateActor {
    const factory = this;
    
    return class extends AggregateActor {
      constructor(ctx: any, id: any) {
        super(ctx, id);
        // Inject dependencies after construction
        this.setupDependencies(
          factory.domainTypes,
          factory.serviceProvider,
          factory.actorProxyFactory,
          factory.serializationService
        );
      }
    };
  }
}