import { ok, err, type Result } from 'neverthrow';
import type { IEvent } from '../events/event';
import { Aggregate } from '../aggregates/aggregate';
import type { PartitionKeys } from '../documents/partition-keys';
import type { EmptyAggregatePayload } from '../aggregates/aggregate';
import type { SekibanError } from '../result/errors';
import { ValidationError } from '../result/errors';
import { SortableUniqueId } from '../documents/sortable-unique-id';
import type { ITypedAggregatePayload } from '../aggregates/aggregate-projector';

/**
 * Projection function that transforms aggregate state based on an event
 */
export type ProjectionFunction<TPayloadUnion extends ITypedAggregatePayload, TEventPayload = any> = (
  state: TPayloadUnion | EmptyAggregatePayload,
  event: TEventPayload
) => TPayloadUnion | EmptyAggregatePayload;

/**
 * Definition structure for a projector
 */
export interface ProjectorDefinition<TPayloadUnion extends ITypedAggregatePayload> {
  /**
   * The aggregate type this projector handles
   */
  aggregateType: string;
  
  /**
   * Function to create the initial empty state
   */
  initialState: () => EmptyAggregatePayload;
  
  /**
   * Map of event types to projection functions
   */
  projections: {
    [eventType: string]: ProjectionFunction<TPayloadUnion>;
  };
}

/**
 * Define a projector with projection functions for different event types
 * 
 * @param definition - The projector definition including aggregate type and projections
 * @returns Projector object with getInitialState and project methods
 * 
 * @example
 * ```typescript
 * const userProjector = defineProjector<UserPayload | DeletedUserPayload>({
 *   aggregateType: 'User',
 *   initialState: () => new EmptyAggregatePayload(),
 *   projections: {
 *     UserCreated: (state, event) => ({
 *       aggregateType: 'User',
 *       userId: event.userId,
 *       name: event.name,
 *       email: event.email
 *     }),
 *     UserDeleted: (state) => ({
 *       aggregateType: 'DeletedUser'
 *     })
 *   }
 * });
 * ```
 */
export function defineProjector<TPayloadUnion extends ITypedAggregatePayload>(
  definition: ProjectorDefinition<TPayloadUnion>
) {
  return {
    aggregateType: definition.aggregateType,
    
    /**
     * Get the initial empty state for a new aggregate
     */
    getInitialState: (partitionKeys: PartitionKeys): Aggregate<EmptyAggregatePayload> => {
      return new Aggregate(
        partitionKeys,
        definition.aggregateType,
        0,
        definition.initialState(),
        null,
        definition.aggregateType,
        1
      );
    },
    
    /**
     * Project an event to update the aggregate state
     * Can transition between different payload types
     */
    project: (
      aggregate: Aggregate<TPayloadUnion | EmptyAggregatePayload>,
      event: IEvent
    ): Result<Aggregate<TPayloadUnion | EmptyAggregatePayload>, SekibanError> => {
      const projection = definition.projections[event.eventType];
      
      // If no projection is defined for this event type, return unchanged aggregate
      if (!projection) {
        return ok(aggregate);
      }
      
      try {
        // Apply the projection to get the new payload
        const newPayload = projection(aggregate.payload, event.payload);
        
        // Create new aggregate with updated payload and incremented version
        const newAggregate = new Aggregate(
          aggregate.partitionKeys,
          aggregate.aggregateType,
          aggregate.version + 1,
          newPayload,
          SortableUniqueId.generate(),
          aggregate.projectorTypeName,
          aggregate.projectorVersion
        );
        
        return ok(newAggregate);
      } catch (error) {
        return err(new ValidationError(
          `Projection failed: ${error instanceof Error ? error.message : 'Unknown error'}`
        ));
      }
    }
  } as const;
}

/**
 * Type helper to extract the projector type from a defineProjector result
 */
export type ProjectorDefinitionType<T> = T extends ReturnType<typeof defineProjector<infer TPayload extends ITypedAggregatePayload>>
  ? ReturnType<typeof defineProjector<TPayload>>
  : never;

/**
 * Type helper to extract the payload union from a projector definition
 */
export type InferProjectorPayload<T> = T extends { project: (aggregate: Aggregate<infer P>, event: any) => any }
  ? P
  : never;