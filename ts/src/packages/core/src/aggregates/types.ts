import { PartitionKeys, SortableUniqueId } from '../documents/index';
import { IEventPayload } from '../events/index';

/**
 * Base interface for aggregate payloads (state)
 */
export interface IAggregatePayload {
  /**
   * The type identifier for the aggregate state
   */
  readonly aggregateType: string;
}

/**
 * Represents an aggregate with its state and metadata
 */
export interface Aggregate<TPayload extends IAggregatePayload = IAggregatePayload> {
  /**
   * The partition keys for this aggregate
   */
  partitionKeys: PartitionKeys;
  
  /**
   * The aggregate type
   */
  aggregateType: string;
  
  /**
   * The current version of the aggregate
   */
  version: number;
  
  /**
   * The aggregate state/payload
   */
  payload: TPayload;
  
  /**
   * The ID of the last event applied
   */
  lastEventId?: SortableUniqueId;
  
  /**
   * Whether this aggregate has been deleted
   */
  isDeleted?: boolean;
}

/**
 * Interface for aggregate factories
 */
export interface IAggregateFactory<TPayload extends IAggregatePayload> {
  /**
   * The aggregate type this factory creates
   */
  aggregateType: string;
  
  /**
   * Creates an empty aggregate
   */
  createEmpty(partitionKeys: PartitionKeys): Aggregate<TPayload>;
  
  /**
   * Creates an aggregate with initial state
   */
  create(partitionKeys: PartitionKeys, payload: TPayload): Aggregate<TPayload>;
}

/**
 * Aggregate loader interface
 */
export interface IAggregateLoader {
  /**
   * Loads an aggregate by its partition keys
   */
  load<TPayload extends IAggregatePayload>(
    partitionKeys: PartitionKeys,
    aggregateType: string
  ): Promise<Aggregate<TPayload> | null>;
  
  /**
   * Loads multiple aggregates
   */
  loadMany<TPayload extends IAggregatePayload>(
    keys: Array<{ partitionKeys: PartitionKeys; aggregateType: string }>
  ): Promise<Array<Aggregate<TPayload> | null>>;
}

/**
 * Represents a projector that applies events to aggregate state
 */
export interface IProjector<TPayload extends IAggregatePayload> {
  /**
   * The aggregate type this projector handles
   */
  aggregateType: string;
  
  /**
   * Applies an event to the aggregate state
   */
  apply(aggregate: Aggregate<TPayload>, event: IEventPayload): Aggregate<TPayload>;
  
  /**
   * Gets the initial state for a new aggregate
   */
  getInitialState(partitionKeys: PartitionKeys): Aggregate<TPayload>;
  
  /**
   * Validates if an event can be applied to the current state
   */
  canApply?(aggregate: Aggregate<TPayload>, event: IEventPayload): boolean;
}

/**
 * Base class for aggregate factories
 */
export abstract class AggregateFactory<TPayload extends IAggregatePayload> 
  implements IAggregateFactory<TPayload> {
  
  constructor(public readonly aggregateType: string) {}

  createEmpty(partitionKeys: PartitionKeys): Aggregate<TPayload> {
    return {
      partitionKeys,
      aggregateType: this.aggregateType,
      version: 0,
      payload: this.getEmptyPayload(),
    };
  }

  create(partitionKeys: PartitionKeys, payload: TPayload): Aggregate<TPayload> {
    return {
      partitionKeys,
      aggregateType: this.aggregateType,
      version: 0,
      payload,
    };
  }

  /**
   * Gets the empty payload for this aggregate type
   */
  protected abstract getEmptyPayload(): TPayload;
}