/**
 * Type definitions for test utilities in the api-multi-projector package
 */

import type { 
  IEventStore,
  IEvent,
  ITypedAggregatePayload,
  IAggregateProjector
} from '@sekiban/core';
import type { DaprClient } from '@dapr/dapr';
import type { HttpMethod } from '@dapr/dapr';

/**
 * Mock state manager interface for testing actors
 */
export interface MockStateManager {
  tryGetState(key: string): Promise<[boolean, any]>;
  setState(key: string, value: any): Promise<void>;
}

/**
 * Mock Dapr client interface for testing
 */
export interface MockDaprClient {
  options: {
    daprHost: string;
    daprPort: string;
    communicationProtocol: string;
    isHTTP: boolean;
  };
  actor: {
    registerReminder: () => Promise<void>;
    unregisterReminder: () => Promise<void>;
  };
}

/**
 * Actor proxy factory interface
 */
export interface IActorProxyFactory {
  createActorProxy<T>(actorId: any, actorType: string): T;
}

/**
 * Projector wrapper returned by domain types
 */
export interface ProjectorWrapper<TPayload extends ITypedAggregatePayload = ITypedAggregatePayload> {
  aggregateTypeName: string;
  projector: IAggregateProjector<TPayload>;
}

/**
 * Domain projector types interface
 */
export interface DomainProjectorTypes {
  getProjectorTypes(): ProjectorWrapper[];
}

/**
 * Extended domain types with convenience methods
 */
export interface ExtendedDomainTypes {
  projectorTypes?: DomainProjectorTypes;
  findCommandDefinition: (name: string) => any;
  findEventDefinition: (name: string) => any;
  findProjectorDefinition: (name: string) => any;
}

/**
 * Actor state storage type
 */
export type ActorState = Record<string, any>;

/**
 * Event store save events method type
 */
export interface EventStoreWithSaveEvents extends IEventStore {
  saveEvents(events: IEvent[]): Promise<void>;
}

/**
 * Properly typed HttpMethod export
 */
export { HttpMethod };