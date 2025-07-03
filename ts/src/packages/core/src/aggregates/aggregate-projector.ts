import { IProjector } from './projector-interface'
import { IAggregatePayload } from './aggregate-payload'
import { IEvent } from '../events/event'
import { Aggregate, createEmptyAggregate, EmptyAggregatePayload } from './aggregate'
import { PartitionKeys } from '../documents/partition-keys'

/**
 * Wrapper class that uses a projector to apply events to aggregates
 */
export class AggregateProjector<TPayload extends IAggregatePayload> {
  constructor(
    private readonly projector: IProjector<TPayload>
  ) {}
  
  /**
   * Project a single event onto an aggregate
   */
  projectEvent(
    aggregate: Aggregate<TPayload>,
    event: IEvent
  ): Aggregate<TPayload> {
    // Apply the event to get new payload
    const newPayload = this.projector.project(aggregate.payload, event.payload)
    
    // Create new aggregate with updated state
    return aggregate.withNewVersion(
      newPayload,
      event.version,
      event.id
    )
  }
  
  /**
   * Project multiple events onto an aggregate
   */
  projectEvents(
    aggregate: Aggregate<TPayload>,
    events: IEvent[]
  ): Aggregate<TPayload> {
    // If no events, return the same aggregate
    if (events.length === 0) {
      return aggregate
    }
    
    // Apply events sequentially
    return events.reduce(
      (currentAggregate, event) => this.projectEvent(currentAggregate, event),
      aggregate
    )
  }
  
  /**
   * Get initial empty aggregate
   */
  getInitialAggregate(
    partitionKeys: PartitionKeys,
    aggregateType: string
  ): Aggregate<EmptyAggregatePayload> {
    return createEmptyAggregate(
      partitionKeys,
      aggregateType,
      this.projector.getTypeName(),
      this.projector.getVersion()
    )
  }
}