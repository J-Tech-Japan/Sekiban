import { 
  IAggregatePayload, 
  Aggregate,
  PartitionKeys,
  SortableUniqueId 
} from '../../../core/src';

/**
 * Fluent builder for creating test aggregates
 */
export class AggregateBuilder<TPayload extends IAggregatePayload> {
  private projectorName: string;
  private partitionKeys?: PartitionKeys;
  private payload?: TPayload;
  private version: number = 1;
  private lastEventId: SortableUniqueId = SortableUniqueId.generate();
  private lastUpdated: Date = new Date();

  constructor(projectorName: string) {
    this.projectorName = projectorName;
  }

  /**
   * Create a builder from an existing aggregate
   */
  static from<T extends IAggregatePayload>(aggregate: Aggregate<T>): AggregateBuilder<T> {
    const builder = new AggregateBuilder<T>(aggregate.projectorName);
    builder.partitionKeys = aggregate.partitionKeys;
    builder.payload = aggregate.payload;
    builder.version = aggregate.version;
    builder.lastEventId = aggregate.lastEventId;
    builder.lastUpdated = new Date(aggregate.lastUpdated);
    return builder;
  }

  /**
   * Set the partition keys
   */
  withPartitionKeys(partitionKeys: PartitionKeys): AggregateBuilder<TPayload> {
    const newBuilder = this.clone();
    newBuilder.partitionKeys = partitionKeys;
    return newBuilder;
  }

  /**
   * Set the aggregate payload
   */
  withPayload(payload: TPayload): AggregateBuilder<TPayload> {
    const newBuilder = this.clone();
    newBuilder.payload = payload;
    return newBuilder;
  }

  /**
   * Update the payload partially
   */
  updatePayload(partial: Partial<TPayload>): AggregateBuilder<TPayload> {
    const newBuilder = this.clone();
    newBuilder.payload = { ...this.payload, ...partial } as TPayload;
    return newBuilder;
  }

  /**
   * Set the aggregate version
   */
  withVersion(version: number): AggregateBuilder<TPayload> {
    const newBuilder = this.clone();
    newBuilder.version = version;
    return newBuilder;
  }

  /**
   * Set the last event ID
   */
  withLastEventId(lastEventId: SortableUniqueId): AggregateBuilder<TPayload> {
    const newBuilder = this.clone();
    newBuilder.lastEventId = lastEventId;
    return newBuilder;
  }

  /**
   * Set the last updated timestamp
   */
  withLastUpdated(lastUpdated: Date): AggregateBuilder<TPayload> {
    const newBuilder = this.clone();
    newBuilder.lastUpdated = lastUpdated;
    return newBuilder;
  }

  /**
   * Mark as a snapshot aggregate
   */
  asSnapshot(): AggregateBuilder<TPayload> {
    // Just ensure it has proper version and timestamps
    return this;
  }

  /**
   * Create an empty aggregate
   */
  asEmpty(): AggregateBuilder<TPayload> {
    const newBuilder = this.clone();
    newBuilder.version = 0;
    newBuilder.payload = undefined;
    return newBuilder;
  }

  /**
   * Build the aggregate
   */
  build(): Aggregate<TPayload> {
    if (!this.partitionKeys) {
      throw new Error('PartitionKeys are required');
    }

    if (this.version < 0) {
      throw new Error('Version must be non-negative');
    }

    const aggregate: Aggregate<TPayload> = {
      projectorName: this.projectorName,
      partitionKeys: this.partitionKeys,
      version: this.version,
      lastEventId: this.lastEventId,
      lastUpdated: this.lastUpdated,
    };

    if (this.payload !== undefined) {
      aggregate.payload = this.payload;
    }

    return aggregate;
  }

  /**
   * Clone the builder
   */
  private clone(): AggregateBuilder<TPayload> {
    const newBuilder = new AggregateBuilder<TPayload>(this.projectorName);
    newBuilder.partitionKeys = this.partitionKeys;
    newBuilder.payload = this.payload;
    newBuilder.version = this.version;
    newBuilder.lastEventId = this.lastEventId;
    newBuilder.lastUpdated = new Date(this.lastUpdated);
    return newBuilder;
  }
}