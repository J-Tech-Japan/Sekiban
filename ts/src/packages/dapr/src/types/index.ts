import { ActorId } from '@dapr/dapr';
import type { ITypedAggregatePayload, PartitionKeys } from '@sekiban/core';
import type { Result } from 'neverthrow';
import type { SnapshotError, SnapshotLoadResult } from '../snapshot/types';

/**
 * Interface for aggregate actors
 */
export interface IAggregateActor<TPayload extends ITypedAggregatePayload> {
  /**
   * Get the current state of the aggregate
   */
  getState(): Promise<Result<SnapshotLoadResult<TPayload>, SnapshotError>>;
  
  /**
   * Apply new events to the aggregate
   */
  applyEvents(events: any[]): Promise<Result<void, SnapshotError>>;
  
  /**
   * Force creation of a snapshot
   */
  createSnapshot(): Promise<Result<void, SnapshotError>>;
}

/**
 * Actor proxy factory interface
 */
export interface IActorProxyFactory {
  /**
   * Create an actor proxy
   */
  createActorProxy<T>(
    actorId: ActorId,
    actorType: string
  ): T;
}

/**
 * Configuration for Dapr integration
 */
export interface DaprConfiguration {
  /** Dapr HTTP endpoint */
  daprHost?: string;
  
  /** Dapr HTTP port */
  daprPort?: number;
  
  /** Actor type name */
  actorType?: string;
  
  /** State store name */
  stateStoreName?: string;
  
  /** Pubsub component name */
  pubsubName?: string;
}

/**
 * Maps aggregate types to their actor implementations
 */
export interface IAggregateActorMap {
  [aggregateType: string]: string; // aggregate type -> actor type
}

/**
 * Factory for creating actor IDs from partition keys
 */
export class ActorIdFactory {
  /**
   * Create an actor ID from partition keys
   */
  static fromPartitionKeys(partitionKeys: PartitionKeys): ActorId {
    // Use a composite key for multi-tenant scenarios
    const id = partitionKeys.rootPartitionKey === 'default'
      ? partitionKeys.aggregateId
      : `${partitionKeys.rootPartitionKey}:${partitionKeys.aggregateId}`;
    
    return new ActorId(id);
  }

  /**
   * Extract partition keys from an actor ID
   */
  static toPartitionKeys(
    actorId: ActorId,
    aggregateType: string
  ): PartitionKeys {
    const idString = actorId.toString();
    const parts = idString.split(':');
    
    if (parts.length === 1) {
      // No tenant prefix
      return {
        aggregateId: parts[0],
        group: aggregateType,
        rootPartitionKey: 'default',
      } as PartitionKeys;
    } else {
      // Has tenant prefix
      return {
        aggregateId: parts.slice(1).join(':'),
        group: aggregateType,
        rootPartitionKey: parts[0],
      } as PartitionKeys;
    }
  }
}

// Domain types are now imported from core package
export type { SekibanDomainTypes } from '@sekiban/core';