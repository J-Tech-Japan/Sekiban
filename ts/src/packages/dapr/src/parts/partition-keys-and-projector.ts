import { PartitionKeys, IAggregateProjector } from '@sekiban/core';
import { Result, ok, err } from 'neverthrow';

/**
 * Holds partition keys and projector information for aggregate actors.
 * This is the TypeScript equivalent of C# Dapr's PartitionKeysAndProjector.
 */
export class PartitionKeysAndProjector<TPayload> {
  constructor(
    public readonly partitionKeys: PartitionKeys,
    public readonly projector: IAggregateProjector<TPayload>
  ) {
    if (!partitionKeys) {
      throw new Error('partitionKeys cannot be null');
    }
    if (!projector) {
      throw new Error('projector cannot be null');
    }
  }

  /**
   * Creates PartitionKeysAndProjector from a grain key string
   * Format: "PartitionKeysString=ProjectorType" (matching C# format)
   */
  static fromGrainKey<TPayload>(
    grainKey: string,
    projectorRegistry: Map<string, IAggregateProjector<TPayload>>
  ): Result<PartitionKeysAndProjector<TPayload>, Error> {
    try {
      // Extract projector type and partition keys from the grain key
      const parts = grainKey.split('=');
      if (parts.length !== 2) {
        return err(new Error(`Invalid grain key format: ${grainKey}`));
      }

      const partitionKeysString = parts[0];
      const projectorTypeName = parts[1];

      // Get projector from registry
      const projector = projectorRegistry.get(projectorTypeName);
      if (!projector) {
        return err(new Error(`Projector not found: ${projectorTypeName}`));
      }

      // Parse partition keys
      const partitionKeys = PartitionKeys.fromPrimaryKeysString(partitionKeysString);

      return ok(new PartitionKeysAndProjector(partitionKeys, projector));
    } catch (error) {
      return err(error instanceof Error ? error : new Error('Unknown error'));
    }
  }

  /**
   * Converts to a grain key string for the projector
   * Matches C# ToProjectorGrainKey()
   */
  toProjectorGrainKey(): string {
    return `${this.partitionKeys.toPrimaryKeysString()}=${this.projector.constructor.name}`;
  }

  /**
   * Converts to a grain key string for the event handler
   * Matches C# ToEventHandlerGrainKey()
   */
  toEventHandlerGrainKey(): string {
    return this.partitionKeys.toPrimaryKeysString();
  }
}