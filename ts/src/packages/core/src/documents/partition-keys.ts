import { generateUuid } from '../utils/index.js';

/**
 * Represents the partition keys for an aggregate
 */
export class PartitionKeys {
  static readonly DEFAULT_ROOT_PARTITION_KEY = 'default';
  static readonly DEFAULT_AGGREGATE_GROUP = 'default';
  
  readonly partitionKey: string;
  
  constructor(
    readonly aggregateId: string,
    readonly group?: string,
    readonly rootPartitionKey?: string
  ) {
    // Build the partition key
    const parts: string[] = [];
    if (rootPartitionKey) {
      parts.push(rootPartitionKey);
    }
    if (group) {
      parts.push(group);
    }
    parts.push(aggregateId);
    
    this.partitionKey = parts.join('-');
  }
  
  /**
   * Creates partition keys with specified values
   */
  static create(
    aggregateId: string,
    group?: string,
    rootPartitionKey?: string
  ): PartitionKeys {
    return new PartitionKeys(aggregateId, group, rootPartitionKey);
  }
  
  /**
   * Generates new partition keys with a new aggregate ID
   */
  static generate(
    group?: string,
    rootPartitionKey?: string
  ): PartitionKeys {
    const aggregateId = generateUuid();
    return new PartitionKeys(aggregateId, group, rootPartitionKey);
  }
  
  /**
   * Creates partition keys for an existing aggregate
   */
  static existing(
    aggregateId: string,
    group?: string,
    rootPartitionKey?: string
  ): PartitionKeys {
    return new PartitionKeys(aggregateId, group, rootPartitionKey);
  }
  
  /**
   * Returns the partition key as a string
   */
  toString(): string {
    return this.partitionKey;
  }
  
  /**
   * Checks if two PartitionKeys are equal
   */
  equals(other: PartitionKeys): boolean {
    return (
      this.aggregateId === other.aggregateId &&
      this.group === other.group &&
      this.rootPartitionKey === other.rootPartitionKey
    );
  }
  
  /**
   * Converts to a composite key string
   */
  static toCompositeKey(keys: PartitionKeys): string {
    const parts = [keys.aggregateId];
    if (keys.group) {
      parts.push(keys.group);
    }
    if (keys.rootPartitionKey) {
      parts.push(keys.rootPartitionKey);
    }
    return parts.join(':');
  }
  
  /**
   * Converts to primary keys string format
   * Matches C# PartitionKeys.ToPrimaryKeysString()
   * Format: {RootPartitionKey}@{Group}@{AggregateId}
   */
  toPrimaryKeysString(): string {
    const rootPartition = this.rootPartitionKey || 'default';
    const group = this.group || 'default';
    return `${rootPartition}@${group}@${this.aggregateId}`;
  }
  
  /**
   * Creates PartitionKeys from primary keys string
   * Matches C# PartitionKeys.FromPrimaryKeysString()
   */
  static fromPrimaryKeysString(primaryKeyString: string): PartitionKeys {
    const parts = primaryKeyString.split('@');
    if (parts.length !== 3) {
      throw new Error(`Invalid primary key string: ${primaryKeyString}`);
    }
    // parts[0] = rootPartitionKey, parts[1] = group, parts[2] = aggregateId
    const aggregateId = parts[2]!; // We know it exists because we checked length === 3
    const group = parts[1] === 'default' ? undefined : parts[1];
    const rootPartitionKey = parts[0] === 'default' ? undefined : parts[0];
    
    return new PartitionKeys(aggregateId, group, rootPartitionKey);
  }
}

/**
 * Legacy interface for backward compatibility
 */
export interface IPartitionKeys {
  aggregateId: string;
  group?: string;
  rootPartitionKey?: string;
}

/**
 * Legacy builder for creating partition keys
 */
export class PartitionKeysBuilder {
  private aggregateId: string;
  private group?: string;
  private rootPartitionKey?: string;

  constructor(aggregateId?: string) {
    this.aggregateId = aggregateId ?? generateUuid();
  }

  static generate(): PartitionKeysBuilder {
    return new PartitionKeysBuilder();
  }

  static existing(aggregateId: string): PartitionKeysBuilder {
    return new PartitionKeysBuilder(aggregateId);
  }

  withGroup(group: string): PartitionKeysBuilder {
    this.group = group;
    return this;
  }

  withRootPartitionKey(rootPartitionKey: string): PartitionKeysBuilder {
    this.rootPartitionKey = rootPartitionKey;
    return this;
  }

  build(): IPartitionKeys {
    return {
      aggregateId: this.aggregateId,
      group: this.group,
      rootPartitionKey: this.rootPartitionKey,
    };
  }
}

/**
 * Legacy utility functions
 */
export const PartitionKeysUtils = {
  generate(rootPartitionKey?: string): IPartitionKeys {
    const builder = PartitionKeysBuilder.generate();
    if (rootPartitionKey) {
      builder.withRootPartitionKey(rootPartitionKey);
    }
    return builder.build();
  },

  existing(aggregateId: string, rootPartitionKey?: string): IPartitionKeys {
    const builder = PartitionKeysBuilder.existing(aggregateId);
    if (rootPartitionKey) {
      builder.withRootPartitionKey(rootPartitionKey);
    }
    return builder.build();
  },

  toCompositeKey(keys: IPartitionKeys): string {
    const parts = [keys.aggregateId];
    if (keys.group) {
      parts.push(keys.group);
    }
    if (keys.rootPartitionKey) {
      parts.push(keys.rootPartitionKey);
    }
    return parts.join(':');
  },

  fromCompositeKey(compositeKey: string): IPartitionKeys {
    const parts = compositeKey.split(':');
    return {
      aggregateId: parts[0]!,
      group: parts[1] || undefined,
      rootPartitionKey: parts[2] || undefined,
    };
  },
};