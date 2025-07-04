import type { Result } from 'neverthrow';
import type { IEventPayload } from '../events/event-payload.js';
import type { IEvent } from '../events/event.js';
import type { IAggregatePayload } from './aggregate-payload.js';
import { Aggregate, EmptyAggregatePayload as EmptyPayload } from './aggregate.js';
import type { PartitionKeys } from '../documents/partition-keys.js';
import type { SekibanError } from '../result/errors.js';

/**
 * Base interface for aggregate payloads with discriminated union support
 */
export interface ITypedAggregatePayload extends IAggregatePayload {
  readonly aggregateType: string;
}

/**
 * Use EmptyAggregatePayload from aggregate.js
 * Note: EmptyPayload now includes aggregateType = 'Empty'
 */
export type EmptyAggregatePayload = EmptyPayload;

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
    event: IEvent
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
  
  getInitialState(partitionKeys: PartitionKeys): Aggregate<EmptyPayload> {
    return new Aggregate(
      partitionKeys,
      this.aggregateTypeName,
      0,
      new EmptyPayload(),
      null,
      this.aggregateTypeName,
      1
    );
  }
  
  abstract project(
    aggregate: Aggregate<TPayloadUnion | EmptyPayload>, 
    event: IEvent
  ): Result<Aggregate<TPayloadUnion | EmptyPayload>, SekibanError>;
  
  abstract canHandle(eventType: string): boolean;
  
  abstract getSupportedPayloadTypes(): string[];
  
  /**
   * Helper method to create a new aggregate with updated payload
   */
  protected createUpdatedAggregate<TNewPayload extends TPayloadUnion>(
    aggregate: Aggregate<TPayloadUnion | EmptyPayload>,
    newPayload: TNewPayload,
    event: IEvent
  ): Aggregate<TNewPayload> {
    return aggregate.withNewVersion(
      newPayload,
      aggregate.version + 1,
      event.id
    ) as Aggregate<TNewPayload>;
  }
  
  /**
   * Helper method to check payload type
   */
  protected isPayloadType<T extends TPayloadUnion>(
    payload: TPayloadUnion | EmptyPayload,
    aggregateType: string
  ): payload is T {
    return 'aggregateType' in payload && payload.aggregateType === aggregateType;
  }
  
  /**
   * Helper method to check if payload is empty
   */
  protected isEmpty(payload: TPayloadUnion | EmptyPayload): payload is EmptyPayload {
    return payload instanceof EmptyPayload;
  }
}