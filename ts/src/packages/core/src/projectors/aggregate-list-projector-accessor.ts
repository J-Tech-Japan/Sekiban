import { IAggregate } from '../aggregates/aggregate'

/**
 * Interface for accessing aggregates in a list projector
 */
export interface IAggregateListProjectorAccessor {
  /**
   * Get all aggregates in the list
   */
  getAggregates(): IAggregate[]
}