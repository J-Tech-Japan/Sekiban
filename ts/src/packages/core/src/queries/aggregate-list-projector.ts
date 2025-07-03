import { IMultiProjector } from './multi-projection'
import { IProjector } from '../aggregates/projector-interface'
import { IAggregatePayload } from '../aggregates/aggregate-payload'
import { IEvent } from '../events/event'
import { IEventPayload } from '../events/event-payload'
import { Aggregate, createEmptyAggregate } from '../aggregates/aggregate'
import { AggregateProjector } from '../aggregates/aggregate-projector'

/**
 * Payload for aggregate list projector
 */
export class AggregateListPayload<TAggregatePayload extends IAggregatePayload = IAggregatePayload> {
  constructor(
    public readonly aggregates: Map<string, Aggregate<TAggregatePayload>> = new Map()
  ) {}
}

/**
 * Multi-projector that maintains a list of all aggregates of a specific type
 */
export class AggregateListProjector<TProjector extends IProjector<IAggregatePayload>> 
  implements IMultiProjector<AggregateListPayload> {
  
  private readonly aggregateProjector: AggregateProjector<IAggregatePayload>
  private readonly aggregateType: string
  
  constructor(
    private readonly projector: TProjector
  ) {
    this.aggregateProjector = new AggregateProjector(projector)
    // Extract aggregate type from projector name (e.g., "UserProjector" -> "User")
    this.aggregateType = projector.getTypeName().replace(/Projector$/, '')
  }
  
  getTypeName(): string {
    return `AggregateList<${this.projector.getTypeName()}>`
  }
  
  getVersion(): number {
    return this.projector.getVersion()
  }
  
  getInitialState(): AggregateListPayload {
    return new AggregateListPayload()
  }
  
  project(state: AggregateListPayload, eventPayload: IEventPayload): AggregateListPayload {
    // Check if this is an IEvent (has required properties)
    const event = eventPayload as unknown as IEvent
    if (!event.partitionKeys || !event.aggregateType || event.version === undefined) {
      return state
    }
    
    // Only process events for our aggregate type
    if (event.aggregateType !== this.aggregateType) {
      return state
    }
    
    const partitionKey = event.partitionKeys.toString()
    const existingAggregate = state.aggregates.get(partitionKey)
    
    let updatedAggregate: Aggregate<IAggregatePayload>
    
    if (existingAggregate) {
      // Update existing aggregate
      updatedAggregate = this.aggregateProjector.projectEvent(existingAggregate, event)
    } else {
      // Create new aggregate
      const emptyAggregate = createEmptyAggregate(
        event.partitionKeys,
        event.aggregateType,
        this.projector.getTypeName(),
        this.projector.getVersion()
      )
      updatedAggregate = this.aggregateProjector.projectEvent(emptyAggregate, event)
    }
    
    // Create new state with updated aggregate
    const newAggregates = new Map(state.aggregates)
    newAggregates.set(partitionKey, updatedAggregate)
    
    return new AggregateListPayload(newAggregates)
  }
}

/**
 * Create an aggregate list projector for a given aggregate projector
 */
export function createAggregateListProjector<TProjector extends IProjector<IAggregatePayload>>(
  projector: TProjector
): AggregateListProjector<TProjector> {
  return new AggregateListProjector(projector)
}