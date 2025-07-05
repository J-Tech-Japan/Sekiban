import type { Result } from 'neverthrow';
import type { 
  ICommand,
  IAggregateProjector,
  ITypedAggregatePayload,
  PartitionKeys,
  Aggregate,
  SekibanError
} from '@sekiban/core';

/**
 * Command and metadata wrapper for Dapr actor communication
 * Equivalent to C# SerializableCommandAndMetadata
 */
export interface SerializableCommandAndMetadata<TPayload extends ITypedAggregatePayload> {
  command: ICommand<TPayload>;
  partitionKeys: PartitionKeys;
  metadata?: Record<string, any>;
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
   */
  executeCommandAsync<TPayload extends ITypedAggregatePayload>(
    commandAndMetadata: SerializableCommandAndMetadata<TPayload>
  ): Promise<SekibanCommandResponse>;
  
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
  executeCommandAsync<TPayload extends ITypedAggregatePayload>(
    command: ICommand<TPayload>
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