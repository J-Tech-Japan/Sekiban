import { generateUuid } from '../utils';

/**
 * Represents the partition keys for an aggregate
 */
export interface PartitionKeys {
  /**
   * The aggregate ID
   */
  aggregateId: string;
  
  /**
   * The group partition key (optional)
   */
  group?: string;
  
  /**
   * The root partition key for multi-tenancy (optional)
   */
  rootPartitionKey?: string;
}

/**
 * Builder for creating partition keys
 */
export class PartitionKeysBuilder {
  private aggregateId: string;
  private group?: string;
  private rootPartitionKey?: string;

  constructor(aggregateId?: string) {
    this.aggregateId = aggregateId ?? generateUuid();
  }

  /**
   * Creates a new PartitionKeysBuilder for generating new aggregate IDs
   */
  static generate(): PartitionKeysBuilder {
    return new PartitionKeysBuilder();
  }

  /**
   * Creates a new PartitionKeysBuilder for an existing aggregate
   */
  static existing(aggregateId: string): PartitionKeysBuilder {
    return new PartitionKeysBuilder(aggregateId);
  }

  /**
   * Sets the group partition key
   */
  withGroup(group: string): PartitionKeysBuilder {
    this.group = group;
    return this;
  }

  /**
   * Sets the root partition key for multi-tenancy
   */
  withRootPartitionKey(rootPartitionKey: string): PartitionKeysBuilder {
    this.rootPartitionKey = rootPartitionKey;
    return this;
  }

  /**
   * Builds the partition keys
   */
  build(): PartitionKeys {
    return {
      aggregateId: this.aggregateId,
      group: this.group,
      rootPartitionKey: this.rootPartitionKey,
    };
  }
}

/**
 * Utility functions for working with partition keys
 */
export const PartitionKeys = {
  /**
   * Creates partition keys for a new aggregate
   */
  generate(rootPartitionKey?: string): PartitionKeys {
    const builder = PartitionKeysBuilder.generate();
    if (rootPartitionKey) {
      builder.withRootPartitionKey(rootPartitionKey);
    }
    return builder.build();
  },

  /**
   * Creates partition keys for an existing aggregate
   */
  existing(aggregateId: string, rootPartitionKey?: string): PartitionKeys {
    const builder = PartitionKeysBuilder.existing(aggregateId);
    if (rootPartitionKey) {
      builder.withRootPartitionKey(rootPartitionKey);
    }
    return builder.build();
  },

  /**
   * Creates a composite partition key string
   */
  toCompositeKey(keys: PartitionKeys): string {
    const parts = [keys.aggregateId];
    if (keys.group) {
      parts.push(keys.group);
    }
    if (keys.rootPartitionKey) {
      parts.push(keys.rootPartitionKey);
    }
    return parts.join(':');
  },

  /**
   * Parses a composite partition key string
   */
  fromCompositeKey(compositeKey: string): PartitionKeys {
    const parts = compositeKey.split(':');
    return {
      aggregateId: parts[0],
      group: parts[1] || undefined,
      rootPartitionKey: parts[2] || undefined,
    };
  },
};