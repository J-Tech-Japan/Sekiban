import { PartitionKeys } from '../documents/partition-keys.js'
import { SortableUniqueId } from '../documents/sortable-unique-id.js'
import { IAggregatePayload } from './aggregate-payload.js'

/**
 * Interface for aggregate entities
 */
export interface IAggregate<TPayload extends IAggregatePayload = IAggregatePayload> {
  /**
   * The partition keys identifying this aggregate
   */
  readonly partitionKeys: PartitionKeys
  
  /**
   * The type name of the aggregate
   */
  readonly aggregateType: string
  
  /**
   * The current version of the aggregate
   */
  readonly version: number
  
  /**
   * The current state/payload of the aggregate
   */
  readonly payload: TPayload
  
  /**
   * The ID of the last event applied to this aggregate
   */
  readonly lastSortableUniqueId: SortableUniqueId | null
  
  /**
   * The name of the projector type used
   */
  readonly projectorTypeName: string
  
  /**
   * The version of the projector
   */
  readonly projectorVersion: number
  
  /**
   * Get the payload type name
   */
  readonly payloadTypeName: string
}

/**
 * Empty aggregate payload marker
 */
export class EmptyAggregatePayload implements IAggregatePayload {
  private readonly _empty = true
  
  /**
   * Type discriminator for empty aggregates
   */
  readonly aggregateType = 'Empty' as const
}

/**
 * Concrete implementation of aggregate
 */
export class Aggregate<TPayload extends IAggregatePayload = IAggregatePayload> 
  implements IAggregate<TPayload> {
  
  constructor(
    public readonly partitionKeys: PartitionKeys,
    public readonly aggregateType: string,
    public readonly version: number,
    public readonly payload: TPayload,
    public readonly lastSortableUniqueId: SortableUniqueId | null,
    public readonly projectorTypeName: string,
    public readonly projectorVersion: number
  ) {
    // Make the aggregate immutable
    Object.freeze(this)
    Object.freeze(this.payload)
  }
  
  /**
   * Get the payload type name
   */
  get payloadTypeName(): string {
    return this.payload.constructor.name
  }
  
  /**
   * Create a new aggregate with updated version and payload
   */
  withNewVersion(
    newPayload: TPayload,
    newVersion: number,
    lastEventId: SortableUniqueId
  ): Aggregate<TPayload> {
    return new Aggregate(
      this.partitionKeys,
      this.aggregateType,
      newVersion,
      newPayload,
      lastEventId,
      this.projectorTypeName,
      this.projectorVersion
    )
  }
}

/**
 * Create an empty aggregate
 */
export function createEmptyAggregate(
  partitionKeys: PartitionKeys,
  aggregateType: string,
  projectorTypeName: string,
  projectorVersion: number
): Aggregate<EmptyAggregatePayload> {
  return new Aggregate(
    partitionKeys,
    aggregateType,
    0,
    new EmptyAggregatePayload(),
    null,
    projectorTypeName,
    projectorVersion
  )
}

/**
 * Check if an aggregate is empty
 */
export function isEmptyAggregate(aggregate: IAggregate): boolean {
  return aggregate.version === 0 && 
         aggregate.payload instanceof EmptyAggregatePayload
}