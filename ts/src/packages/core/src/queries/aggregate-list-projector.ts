import { Result, ok, err } from 'neverthrow'
import { IMultiProjector } from '../projectors/multi-projector'
import { IAggregateListProjectorAccessor } from '../projectors/aggregate-list-projector-accessor'
import { IAggregate, Aggregate, EmptyAggregatePayload } from '../aggregates/aggregate'
import { IAggregateProjector } from '../aggregates/aggregate-projector'
import { IEvent } from '../events/event'
import { PartitionKeys } from '../documents/partition-keys'
import { SekibanError } from '../result/errors'

/**
 * AggregateListProjector that maintains a list of aggregates
 * Projects events across multiple aggregates of the same type
 */
export class AggregateListProjector<TAggregateProjector extends IAggregateProjector<any>>
  implements IMultiProjector<AggregateListProjector<TAggregateProjector>>, IAggregateListProjectorAccessor {
  
  private readonly aggregates: Map<string, Aggregate>
  
  constructor(
    aggregates: Map<string, Aggregate> = new Map(),
    private readonly projectorFactory: () => TAggregateProjector
  ) {
    this.aggregates = new Map(aggregates)
  }
  
  /**
   * Get all aggregates as an array
   */
  getAggregates(): IAggregate[] {
    return Array.from(this.aggregates.values())
  }
  
  /**
   * Generate initial empty state
   */
  generateInitialPayload(): AggregateListProjector<TAggregateProjector> {
    return new AggregateListProjector<TAggregateProjector>(new Map(), this.projectorFactory)
  }
  
  /**
   * Project an event to update the state
   */
  project(
    payload: AggregateListProjector<TAggregateProjector>, 
    event: IEvent
  ): Result<AggregateListProjector<TAggregateProjector>, SekibanError> {
    const projector = this.projectorFactory()
    const partitionKeys = event.partitionKeys
    
    // Use aggregateId as partition key if partitionKey is not available
    const partitionKey = partitionKeys.partitionKey || partitionKeys.aggregateId || event.aggregateId
    
    console.log('[AggregateListProjector.project] Processing event:', {
      eventType: event.eventType,
      aggregateId: event.aggregateId,
      partitionKeysType: typeof partitionKeys,
      partitionKeys: partitionKeys,
      calculatedPartitionKey: partitionKey,
      currentAggregatesSize: payload.aggregates.size,
      currentAggregatesKeys: Array.from(payload.aggregates.keys())
    })
    
    // Get existing aggregate or create empty one
    const existingAggregate = payload.aggregates.get(partitionKey)
    const aggregate = existingAggregate ?? Aggregate.emptyFromPartitionKeys(partitionKeys)
    
    // Project the event onto the aggregate
    const projectedResult = projector.project(aggregate, event)
    
    return projectedResult.match(
      (projectedAggregate) => {
        // If the result is empty aggregate, don't update the list
        if (projectedAggregate.payload instanceof EmptyAggregatePayload) {
          console.log('[AggregateListProjector.project] Skipping empty aggregate')
          return ok(payload)
        }
        
        console.log('[AggregateListProjector.project] Updating aggregate:', {
          partitionKey,
          aggregateType: projectedAggregate.payload?.aggregateType,
          payloadType: projectedAggregate.payload?.constructor.name,
          previousSize: payload.aggregates.size
        })
        
        // Create new map with updated aggregate
        const newAggregates = new Map(payload.aggregates)
        newAggregates.set(partitionKey, projectedAggregate)
        
        console.log('[AggregateListProjector.project] Created new projector with size:', newAggregates.size)
        
        return ok(new AggregateListProjector(newAggregates, this.projectorFactory))
      },
      (error) => {
        console.error('[AggregateListProjector.project] Projection error:', error)
        return err(error)
      }
    )
  }
  
  /**
   * Get the name of this multi-projector
   */
  getMultiProjectorName(): string {
    const projector = this.projectorFactory()
    const projectorName = projector.aggregateTypeName.toLowerCase()
    const multiProjectorName = `aggregatelistprojector-${projectorName}`
    console.log('[AggregateListProjector.getMultiProjectorName]:', {
      aggregateTypeName: projector.aggregateTypeName,
      projectorNameLowercase: projectorName,
      generatedMultiProjectorName: multiProjectorName
    })
    return multiProjectorName
  }
  
  /**
   * Get the version
   */
  getVersion(): string {
    return 'initial'
  }
  
  /**
   * Create an AggregateListProjector for a specific aggregate projector type
   */
  static create<TProjector extends IAggregateProjector<any>>(
    projectorFactory: () => TProjector
  ): AggregateListProjector<TProjector> {
    return new AggregateListProjector(new Map(), projectorFactory)
  }
  
  /**
   * Get the multi-projector name for a specific aggregate projector type
   */
  static getMultiProjectorName<TProjector extends IAggregateProjector<any>>(
    projectorFactory: () => TProjector
  ): string {
    const projector = projectorFactory()
    const projectorName = projector.aggregateTypeName.toLowerCase()
    return `aggregatelistprojector-${projectorName}`
  }
}