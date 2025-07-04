import { Result, ok, err } from 'neverthrow';
import { PartitionKeys } from '../documents/partition-keys.js';
import { SortableUniqueId } from '../documents/sortable-unique-id.js';
import { Aggregate } from '../aggregates/aggregate.js';

/**
 * Optional value wrapper
 */
export class OptionalValue<T> {
  private constructor(
    private readonly hasValue: boolean,
    private readonly value?: T
  ) {}

  static empty<T>(): OptionalValue<T> {
    return new OptionalValue<T>(false);
  }

  static fromValue<T>(value: T): OptionalValue<T> {
    return new OptionalValue<T>(true, value);
  }

  static fromNullableValue<T>(value: T | null | undefined): OptionalValue<T> {
    return value === null || value === undefined
      ? OptionalValue.empty<T>()
      : OptionalValue.fromValue(value);
  }

  get hasValueProperty(): boolean {
    return this.hasValue;
  }

  getValue(): T {
    if (!this.hasValue) {
      throw new Error('OptionalValue has no value');
    }
    return this.value!;
  }
}

/**
 * Interface for sortable ID conditions
 */
export interface ISortableIdCondition {
  outsideOfRange(toCompare: SortableUniqueId): boolean;
}

/**
 * No condition - always returns false
 */
export class SortableIdConditionNone implements ISortableIdCondition {
  outsideOfRange(toCompare: SortableUniqueId): boolean {
    return false;
  }

  static readonly none = new SortableIdConditionNone();
}

/**
 * Since condition - returns true if the ID is earlier than or equal to the given ID
 * This filters out events that come before or at the specified ID
 */
export class SinceSortableIdCondition implements ISortableIdCondition {
  constructor(private readonly sortableUniqueId: SortableUniqueId) {}

  outsideOfRange(toCompare: SortableUniqueId): boolean {
    // Return true if toCompare is earlier than or equal to the since ID
    return SortableUniqueId.compare(toCompare, this.sortableUniqueId) <= 0;
  }
}

/**
 * Between condition - returns true if the ID is outside the range
 */
export class BetweenSortableIdCondition implements ISortableIdCondition {
  constructor(
    private readonly start: SortableUniqueId,
    private readonly end: SortableUniqueId
  ) {}

  outsideOfRange(toCompare: SortableUniqueId): boolean {
    return (
      SortableUniqueId.compare(this.start, toCompare) > 0 ||
      SortableUniqueId.compare(this.end, toCompare) < 0
    );
  }
}

/**
 * Factory methods for sortable ID conditions
 */
export class SortableIdCondition {
  static none(): ISortableIdCondition {
    return SortableIdConditionNone.none;
  }

  static since(sinceSortableId: SortableUniqueId): ISortableIdCondition {
    return new SinceSortableIdCondition(sinceSortableId);
  }

  static between(start: SortableUniqueId, end: SortableUniqueId): ISortableIdCondition {
    // Ensure start is earlier than end
    if (SortableUniqueId.compare(start, end) > 0) {
      return new BetweenSortableIdCondition(end, start);
    }
    return new BetweenSortableIdCondition(start, end);
  }

  static fromState(state: Aggregate | null | undefined): ISortableIdCondition {
    if (state?.lastSortableUniqueId) {
      return SortableIdCondition.since(state.lastSortableUniqueId);
    }
    return SortableIdCondition.none();
  }
}

/**
 * Interface for aggregate streams
 */
export interface IAggregatesStream {
  getStreamNames(): string[];
  getSingleStreamName(): Result<string, Error>;
}

/**
 * Aggregate group stream
 */
export class AggregateGroupStream implements IAggregatesStream {
  constructor(private readonly aggregateGroup: string) {}

  getStreamNames(): string[] {
    return [this.aggregateGroup];
  }

  getSingleStreamName(): Result<string, Error> {
    const names = this.getStreamNames();
    if (names.length !== 1) {
      return err(new Error('Stream names is not set'));
    }
    return ok(names[0]!);
  }
}

/**
 * Event retrieval information
 */
export class EventRetrievalInfo {
  readonly maxCount: OptionalValue<number>;

  constructor(
    readonly rootPartitionKey: OptionalValue<string>,
    readonly aggregateStream: OptionalValue<IAggregatesStream>,
    readonly aggregateId: OptionalValue<string>,
    readonly sortableIdCondition: ISortableIdCondition,
    maxCount?: OptionalValue<number>
  ) {
    this.maxCount = maxCount || OptionalValue.empty<number>();
  }

  static fromNullableValues(
    rootPartitionKey: string | null | undefined,
    aggregatesStream: IAggregatesStream,
    aggregateId: string | null | undefined,
    sortableIdCondition: ISortableIdCondition,
    maxCount?: number | null
  ): EventRetrievalInfo {
    // If rootPartitionKey is not provided but aggregateId is, use default
    const rootPartition = !rootPartitionKey && aggregateId
      ? OptionalValue.fromValue(PartitionKeys.DEFAULT_ROOT_PARTITION_KEY)
      : OptionalValue.fromNullableValue(rootPartitionKey);

    return new EventRetrievalInfo(
      rootPartition,
      OptionalValue.fromValue(aggregatesStream),
      OptionalValue.fromNullableValue(aggregateId),
      sortableIdCondition,
      OptionalValue.fromNullableValue(maxCount)
    );
  }

  static all(): EventRetrievalInfo {
    return new EventRetrievalInfo(
      OptionalValue.empty<string>(),
      OptionalValue.empty<IAggregatesStream>(),
      OptionalValue.empty<string>(),
      SortableIdConditionNone.none
    );
  }

  static fromPartitionKeys(partitionKeys: PartitionKeys): EventRetrievalInfo {
    return new EventRetrievalInfo(
      OptionalValue.fromValue(partitionKeys.rootPartitionKey || PartitionKeys.DEFAULT_ROOT_PARTITION_KEY),
      OptionalValue.fromValue(new AggregateGroupStream(partitionKeys.group || PartitionKeys.DEFAULT_AGGREGATE_GROUP)),
      OptionalValue.fromValue(partitionKeys.aggregateId),
      SortableIdConditionNone.none
    );
  }

  getIsPartition(): boolean {
    return this.aggregateId.hasValueProperty;
  }

  hasAggregateStream(): boolean {
    return this.aggregateStream.hasValueProperty && 
           this.aggregateStream.getValue().getStreamNames().length > 0;
  }

  hasRootPartitionKey(): boolean {
    return this.rootPartitionKey.hasValueProperty;
  }

  getPartitionKey(): Result<string, Error> {
    if (!this.getIsPartition()) {
      return err(new Error('Partition key is not set'));
    }
    if (!this.hasAggregateStream()) {
      return err(new Error('Aggregate stream is not set'));
    }
    
    const singleStreamResult = this.aggregateStream.getValue().getSingleStreamName();
    if (singleStreamResult.isErr()) {
      return err(singleStreamResult.error);
    }
    
    if (!this.hasRootPartitionKey()) {
      return err(new Error('Root partition key is not set'));
    }

    const partitionKeys = PartitionKeys.existing(
      this.aggregateId.getValue(),
      singleStreamResult.value,
      this.rootPartitionKey.getValue()
    );
    
    return ok(partitionKeys.toPrimaryKeysString());
  }
}