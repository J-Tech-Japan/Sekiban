/**
 * Type definitions for domain and infrastructure types
 */

import type { IEventStore, IEvent } from '@sekiban/core';
import type { ActorId } from '@dapr/dapr';

/**
 * Event data structure from pub/sub
 */
export interface PubSubEventData {
  id: string;
  sortableUniqueId: string;
  aggregateId: string;
  aggregateType: string;
  eventType: string;
  payload: Record<string, unknown>;
  version: number;
  partitionKeys: {
    aggregateId: string;
    aggregateGroup: string;
    partitionKey: string;
    rootPartitionKey?: string;
  };
  metadata: Record<string, unknown>;
  createdAt: string;
  [key: string]: unknown;
}

/**
 * Serialization service interface
 */
export interface SerializationService {
  deserializeAggregateAsync(surrogate: unknown): Promise<unknown>;
  serializeAggregateAsync(aggregate: unknown): Promise<unknown>;
}

/**
 * Actor proxy factory interface
 */
export interface ActorProxyFactory {
  createActorProxy(actorId: ActorId | string, actorType: string): unknown;
}

/**
 * Query result item
 */
export interface QueryResultItem {
  id: string;
  title?: string;
  priority?: string;
  [key: string]: unknown;
}

/**
 * List query response
 */
export interface ListQueryResponse {
  isSuccess: boolean;
  items: QueryResultItem[];
  total?: number;
  error?: string;
}