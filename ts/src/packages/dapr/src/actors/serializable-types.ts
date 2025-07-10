import type { Metadata, PartitionKeys } from '@sekiban/core';

/**
 * Serializable command and metadata for actor communication
 * Matches C# SerializableCommandAndMetadata
 */
export interface SerializableCommandAndMetadata {
  // CommandMetadata flat properties
  commandId: string;
  causationId: string;
  correlationId: string;
  executedUser: string;
  
  // Command information
  commandTypeName: string;
  projectorTypeName: string;
  aggregatePayloadTypeName: string;
  
  // Command data (not compressed in TS version for simplicity)
  commandData: any;
  
  // Application version info
  commandAssemblyVersion: string;
}

/**
 * Serializable command response
 * Matches C# SerializableCommandResponse
 */
export interface SerializableCommandResponse {
  aggregateId: string;
  group: string;
  rootPartitionKey: string;
  version: number;
  events: SerializableEvent[];
  error?: any;
}

export interface SerializableEvent {
  eventType: string;
  payload: any;
}

/**
 * Serializable aggregate
 * Matches C# SerializableAggregate
 */
export interface SerializableAggregate {
  partitionKeys: PartitionKeys;
  aggregate: any;
  lastSortableUniqueId: string;
}

/**
 * Create metadata from command metadata
 */
export function createMetadata(metadata?: Partial<Metadata>): Metadata {
  return {
    timestamp: new Date(),
    requestId: metadata?.requestId || crypto.randomUUID(),
    custom: metadata?.custom || {}
  };
}

/**
 * Create serializable command and metadata
 */
export function createSerializableCommandAndMetadata(
  command: any,
  commandData: any,
  metadata: Metadata,
  commandTypeName: string,
  projectorTypeName: string,
  aggregatePayloadTypeName: string = ''
): SerializableCommandAndMetadata {
  return {
    // Flatten metadata
    commandId: crypto.randomUUID(),
    causationId: metadata.requestId || '',
    correlationId: metadata.requestId || '',
    executedUser: metadata.custom?.user || 'system',
    
    // Command info
    commandTypeName,
    projectorTypeName,
    aggregatePayloadTypeName,
    
    // Command data
    commandData,
    
    // Version
    commandAssemblyVersion: '1.0.0'
  };
}