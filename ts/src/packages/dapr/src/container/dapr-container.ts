import { createContainer, asValue, asFunction, InjectionMode, AwilixContainer } from 'awilix';
import type { SekibanDomainTypes } from '@sekiban/core';
import type { IActorProxyFactory } from '../types/index.js';

/**
 * Dependency interface for Dapr actors
 */
export interface DaprActorDependencies {
  domainTypes: SekibanDomainTypes;
  serviceProvider: any;
  actorProxyFactory: IActorProxyFactory;
  serializationService: any;
  eventStore?: any;
  eventHandlerDirectCall?: (actorId: string, method: string, args: any[]) => Promise<any>;
  daprClient?: any;
  configuration?: {
    pubSubName?: string;
    eventTopicName?: string;
    [key: string]: any;
  };
}

/**
 * Awilix container for Dapr actor dependencies
 */
let container: AwilixContainer<DaprActorDependencies> | null = null;

/**
 * Initialize the Awilix container with dependencies
 */
export function initializeDaprContainer(dependencies: DaprActorDependencies): void {
  container = createContainer<DaprActorDependencies>({
    injectionMode: InjectionMode.PROXY,
    strict: true
  });

  // Register all dependencies
  container.register({
    domainTypes: asValue(dependencies.domainTypes),
    serviceProvider: asValue(dependencies.serviceProvider),
    actorProxyFactory: asValue(dependencies.actorProxyFactory),
    serializationService: asValue(dependencies.serializationService),
    eventStore: asValue(dependencies.eventStore),
    eventHandlerDirectCall: asValue(dependencies.eventHandlerDirectCall),
    daprClient: asValue(dependencies.daprClient),
    configuration: asValue(dependencies.configuration)
  });

  // Container initialized with all dependencies
}

/**
 * Get the Dapr container instance
 * @throws Error if container is not initialized
 */
export function getDaprContainer(): AwilixContainer<DaprActorDependencies> {
  if (!container) {
    throw new Error('DaprContainer not initialized. Call initializeDaprContainer() first.');
  }
  return container;
}

/**
 * Get the cradle (resolved dependencies) from the container
 * @throws Error if container is not initialized
 */
export function getDaprCradle(): DaprActorDependencies {
  return getDaprContainer().cradle;
}

/**
 * Dispose the container
 */
export function disposeDaprContainer(): void {
  if (container) {
    container.dispose();
    container = null;
  }
}