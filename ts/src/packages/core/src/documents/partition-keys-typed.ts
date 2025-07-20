import { generateUuid } from '../utils/index';
import { PartitionKeys } from './partition-keys';
import type { IAggregateProjector, ITypedAggregatePayload } from '../aggregates/aggregate-projector';

/**
 * Type-safe partition keys factory that uses projector type for group name
 */
export class TypedPartitionKeys {
  /**
   * Generates new partition keys with a new aggregate ID
   * Group name is derived from the projector's aggregateTypeName
   */
  static Generate<TProjector extends IAggregateProjector<TPayloadUnion>, TPayloadUnion extends ITypedAggregatePayload>(
    projectorClass: new () => TProjector,
    rootPartitionKey?: string
  ): PartitionKeys {
    const projector = new projectorClass();
    const aggregateId = generateUuid();
    return new PartitionKeys(aggregateId, projector.aggregateTypeName, rootPartitionKey);
  }
  
  /**
   * Creates partition keys for an existing aggregate
   * Group name is derived from the projector's aggregateTypeName
   */
  static Existing<TProjector extends IAggregateProjector<TPayloadUnion>, TPayloadUnion extends ITypedAggregatePayload>(
    projectorClass: new () => TProjector,
    aggregateId: string,
    rootPartitionKey?: string
  ): PartitionKeys {
    const projector = new projectorClass();
    return new PartitionKeys(aggregateId, projector.aggregateTypeName, rootPartitionKey);
  }
}

/**
 * Re-export the original PartitionKeys class for backward compatibility
 */
export { PartitionKeys };

/**
 * Convenience functions matching C# style
 */
export const PartitionKeysFactory = {
  /**
   * Generate new partition keys with type safety
   * @example PartitionKeysFactory.generate(UserProjector)
   * @example PartitionKeysFactory.generate(UserProjector, 'tenant-123')
   */
  generate<TProjector extends IAggregateProjector<TPayloadUnion>, TPayloadUnion extends ITypedAggregatePayload>(
    projectorClass: new () => TProjector,
    rootPartitionKey?: string
  ): PartitionKeys {
    return TypedPartitionKeys.Generate(projectorClass, rootPartitionKey);
  },
  
  /**
   * Create partition keys for existing aggregate with type safety
   * @example PartitionKeysFactory.existing(UserProjector, 'user-123')
   * @example PartitionKeysFactory.existing(UserProjector, 'user-123', 'tenant-123')
   */
  existing<TProjector extends IAggregateProjector<TPayloadUnion>, TPayloadUnion extends ITypedAggregatePayload>(
    projectorClass: new () => TProjector,
    aggregateId: string,
    rootPartitionKey?: string
  ): PartitionKeys {
    return TypedPartitionKeys.Existing(projectorClass, aggregateId, rootPartitionKey);
  }
};