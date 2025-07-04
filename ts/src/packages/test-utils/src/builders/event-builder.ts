import { 
  IEventPayload, 
  EventDocument, 
  PartitionKeys,
  SortableUniqueId,
  Metadata
} from '../../../core/src';

/**
 * Fluent builder for creating test events
 */
export class EventBuilder<TPayload extends IEventPayload> {
  private eventType: string;
  private payload?: TPayload;
  private version: number = 1;
  private timestamp: Date = new Date();
  private sortableUniqueId: SortableUniqueId = SortableUniqueId.generate();
  private partitionKeys?: PartitionKeys;
  private metadata?: Metadata;

  constructor(eventType: string) {
    this.eventType = eventType;
  }

  /**
   * Create a builder from an existing event
   */
  static from<T extends IEventPayload>(event: EventDocument<T>): EventBuilder<T> {
    const builder = new EventBuilder<T>(event.eventType);
    builder.payload = event.payload;
    builder.version = event.version;
    builder.timestamp = new Date(event.timestamp);
    builder.sortableUniqueId = SortableUniqueId.generate(); // Generate new ID
    builder.partitionKeys = event.partitionKeys;
    builder.metadata = event.metadata ? { ...event.metadata } : undefined;
    return builder;
  }

  /**
   * Set the event payload
   */
  withPayload(payload: TPayload): EventBuilder<TPayload> {
    this.payload = payload;
    return this;
  }

  /**
   * Update the payload partially
   */
  updatePayload(partial: Partial<TPayload>): EventBuilder<TPayload> {
    this.payload = { ...this.payload, ...partial } as TPayload;
    return this;
  }

  /**
   * Set the event version
   */
  withVersion(version: number): EventBuilder<TPayload> {
    this.version = version;
    return this;
  }

  /**
   * Set the event timestamp
   */
  withTimestamp(timestamp: Date): EventBuilder<TPayload> {
    this.timestamp = timestamp;
    return this;
  }

  /**
   * Set timestamp relative to another timestamp
   */
  withTimestampAfter(baseTimestamp: Date, milliseconds: number): EventBuilder<TPayload> {
    this.timestamp = new Date(baseTimestamp.getTime() + milliseconds);
    return this;
  }

  /**
   * Set the sortable unique ID
   */
  withSortableUniqueId(sortableUniqueId: SortableUniqueId): EventBuilder<TPayload> {
    // Don't clone to preserve the specific ID
    this.sortableUniqueId = sortableUniqueId;
    return this;
  }

  /**
   * Set the partition keys
   */
  withPartitionKeys(partitionKeys: PartitionKeys): EventBuilder<TPayload> {
    this.partitionKeys = partitionKeys;
    return this;
  }

  /**
   * Set metadata
   */
  withMetadata(metadata: Metadata): EventBuilder<TPayload> {
    this.metadata = metadata;
    return this;
  }

  /**
   * Build a single event
   */
  build(): EventDocument<TPayload> {
    if (!this.payload) {
      throw new Error('Payload is required');
    }

    if (this.version <= 0) {
      throw new Error('Version must be positive');
    }

    const event: EventDocument<TPayload> = {
      id: this.sortableUniqueId.toString(),
      eventType: this.eventType,
      payload: this.payload,
      version: this.version,
      timestamp: this.timestamp,
      sortableUniqueId: this.sortableUniqueId,
    };

    if (this.partitionKeys) {
      event.partitionKeys = this.partitionKeys;
    }

    if (this.metadata) {
      event.metadata = this.metadata;
    }

    return event;
  }

  /**
   * Build multiple events with incremental versions
   */
  buildMany(payloads: TPayload[]): EventDocument<TPayload>[] {
    const baseVersion = this.version;
    return payloads.map((payload, index) => {
      this.payload = payload;
      this.version = baseVersion + index;
      this.sortableUniqueId = SortableUniqueId.generate();
      return this.build();
    });
  }

  /**
   * Clone the builder
   */
  private clone(): EventBuilder<TPayload> {
    const newBuilder = new EventBuilder<TPayload>(this.eventType);
    newBuilder.payload = this.payload;
    newBuilder.version = this.version;
    newBuilder.timestamp = new Date(this.timestamp);
    newBuilder.sortableUniqueId = SortableUniqueId.generate(); // Generate new ID for clone
    newBuilder.partitionKeys = this.partitionKeys;
    newBuilder.metadata = this.metadata ? { ...this.metadata } : undefined;
    return newBuilder;
  }
}