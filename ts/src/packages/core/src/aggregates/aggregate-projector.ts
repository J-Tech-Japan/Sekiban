import type { Result } from 'neverthrow';
import type { IEventPayload } from '../events/event-payload.js';
import type { IAggregatePayload } from './aggregate-payload.js';
import type { Aggregate } from './aggregate.js';
import type { PartitionKeys } from '../partition-keys/partition-keys.js';
import type { SekibanError } from '../errors/sekiban-error.js';

/**
 * Base interface for aggregate payloads with discriminated union support
 */
export interface ITypedAggregatePayload extends IAggregatePayload {
  readonly aggregateType: string;
}

/**
 * Empty aggregate payload for initial state
 */
export interface EmptyAggregatePayload extends ITypedAggregatePayload {
  readonly aggregateType: 'Empty';
}

/**
 * Aggregate projector interface that can handle multiple payload types
 * This supports state machine patterns where aggregates transition between different payload types
 */
export interface IAggregateProjector<TPayloadUnion extends ITypedAggregatePayload> {
  readonly aggregateTypeName: string;
  
  /**
   * Get the initial empty state for a new aggregate
   */
  getInitialState(partitionKeys: PartitionKeys): Aggregate<EmptyAggregatePayload>;
  
  /**
   * Project an event to update the aggregate state
   * Can transition between different payload types
   */
  project(
    aggregate: Aggregate<TPayloadUnion | EmptyAggregatePayload>, 
    event: IEventPayload
  ): Result<Aggregate<TPayloadUnion | EmptyAggregatePayload>, SekibanError>;
  
  /**
   * Check if this projector can handle the given event type
   */
  canHandle(eventType: string): boolean;
  
  /**
   * Get supported payload types
   */
  getSupportedPayloadTypes(): string[];
}

/**
 * Base abstract class for aggregate projectors
 */
export abstract class AggregateProjector<TPayloadUnion extends ITypedAggregatePayload> 
  implements IAggregateProjector<TPayloadUnion> {
  
  abstract readonly aggregateTypeName: string;
  
  getInitialState(partitionKeys: PartitionKeys): Aggregate<EmptyAggregatePayload> {
    return {
      partitionKeys,
      payload: {
        aggregateType: 'Empty'
      } as EmptyAggregatePayload,
      version: 0,
      lastEventId: null,
      appliedEvents: []
    };
  }
  
  abstract project(
    aggregate: Aggregate<TPayloadUnion | EmptyAggregatePayload>, 
    event: IEventPayload
  ): Result<Aggregate<TPayloadUnion | EmptyAggregatePayload>, SekibanError>;
  
  abstract canHandle(eventType: string): boolean;
  
  abstract getSupportedPayloadTypes(): string[];
  
  /**
   * Helper method to create a new aggregate with updated payload
   */
  protected createUpdatedAggregate<TNewPayload extends TPayloadUnion>(
    aggregate: Aggregate<TPayloadUnion | EmptyAggregatePayload>,
    newPayload: TNewPayload,
    event: IEventPayload
  ): Aggregate<TNewPayload> {
    return {
      partitionKeys: aggregate.partitionKeys,
      payload: newPayload,
      version: aggregate.version + 1,
      lastEventId: event.eventId || null,
      appliedEvents: [...aggregate.appliedEvents, event]
    };
  }
  
  /**
   * Helper method to check payload type
   */
  protected isPayloadType<T extends TPayloadUnion>(
    payload: TPayloadUnion | EmptyAggregatePayload,
    aggregateType: string
  ): payload is T {
    return payload.aggregateType === aggregateType;
  }
  
  /**
   * Helper method to check if payload is empty
   */
  protected isEmpty(payload: TPayloadUnion | EmptyAggregatePayload): payload is EmptyAggregatePayload {
    return payload.aggregateType === 'Empty';
  }
}