import type { Result } from 'neverthrow';
import type { 
  ICommandWithHandler,
  IAggregateProjector,
  ITypedAggregatePayload,
  EmptyAggregatePayload,
  PartitionKeys,
  Aggregate,
  SekibanError,
  Metadata
} from '@sekiban/core';

/**
 * Command and metadata wrapper for Dapr actor communication
 * Equivalent to C# SerializableCommandAndMetadata
 * Updated to work with ICommandWithHandler
 */
export interface SerializableCommandAndMetadata<
  TCommand,
  TProjector extends IAggregateProjector<TPayloadUnion>,
  TPayloadUnion extends ITypedAggregatePayload,
  TAggregatePayload extends TPayloadUnion | EmptyAggregatePayload = TPayloadUnion | EmptyAggregatePayload
> {
  commandType: string;
  commandData: TCommand;
  partitionKeys: PartitionKeys;
  metadata?: Metadata;
}

/**
 * Response from command execution through Dapr
 * Matches C# SekibanCommandResponse
 */
export interface SekibanCommandResponse {
  aggregateId: string;
  lastSortableUniqueId: string;
  success: boolean;
  errorMessage?: string;
  metadata?: Record<string, any>;
}

/**
 * Interface for Dapr AggregateActor proxy
 * Defines the contract for actor communication
 */
export interface IDaprAggregateActorProxy {
  /**
   * Execute command through the actor
   * Returns either SekibanCommandResponse object or JSON string that can be parsed to SekibanCommandResponse
   */
  executeCommandAsync<
    TCommand,
    TProjector extends IAggregateProjector<TPayloadUnion>,
    TPayloadUnion extends ITypedAggregatePayload,
    TAggregatePayload extends TPayloadUnion | EmptyAggregatePayload = TPayloadUnion | EmptyAggregatePayload
  >(
    commandAndMetadata: SerializableCommandAndMetadata<TCommand, TProjector, TPayloadUnion, TAggregatePayload>
  ): Promise<SekibanCommandResponse | string>;
  
  /**
   * Execute query through the actor
   */
  queryAsync<T>(query: any): Promise<T>;
  
  /**
   * Load aggregate state from the actor
   */
  loadAggregateAsync<TPayload extends ITypedAggregatePayload>(
    partitionKeys: PartitionKeys
  ): Promise<Aggregate<TPayload>>;
}

/**
 * Configuration for Sekiban Dapr integration
 * Matches C# DaprSekibanOptions
 */
export interface DaprSekibanConfiguration {
  stateStoreName: string;
  pubSubName: string;
  eventTopicName: string;
  actorType: string;
  
  // Optional configuration
  actorIdPrefix?: string;
  maxConcurrentRequests?: number;
  requestTimeoutMs?: number;
  retryAttempts?: number;
  circuitBreakerThreshold?: number;
}

/**
 * Main interface for Sekiban Dapr Executor
 * Equivalent to C# ISekibanExecutor but with Dapr-specific features
 */
export interface ISekibanDaprExecutor {
  /**
   * Execute command through Dapr AggregateActor
   */
  executeCommandAsync<
    TCommand,
    TProjector extends IAggregateProjector<TPayloadUnion>,
    TPayloadUnion extends ITypedAggregatePayload,
    TAggregatePayload extends TPayloadUnion | EmptyAggregatePayload = TPayloadUnion | EmptyAggregatePayload
  >(
    command: ICommandWithHandler<TCommand, TProjector, TPayloadUnion, TAggregatePayload>,
    commandData: TCommand,
    metadata?: Metadata
  ): Promise<Result<SekibanCommandResponse, SekibanError>>;
  
  /**
   * Execute query through Dapr actor
   */
  queryAsync<T>(query: any): Promise<Result<T, SekibanError>>;
  
  /**
   * Load aggregate from Dapr actor
   */
  loadAggregateAsync<TPayload extends ITypedAggregatePayload>(
    projector: IAggregateProjector<TPayload>,
    partitionKeys: PartitionKeys
  ): Promise<Result<Aggregate<TPayload>, SekibanError>>;
  
  /**
   * Get configuration
   */
  getConfiguration(): DaprSekibanConfiguration;
  
  /**
   * Check if projector is registered
   */
  hasProjector(aggregateTypeName: string): boolean;
}