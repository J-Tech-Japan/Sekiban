import { describe, it, expect } from 'vitest';
import { EventRetrievalInfo, OptionalValue, AggregateGroupStream, SortableIdCondition } from '../../events/event-retrieval-info';
import { PartitionKeys } from '../../documents/partition-keys';
import { SortableUniqueId } from '../../documents/sortable-unique-id';

describe('EventRetrievalInfo', () => {
  describe('constructor', () => {
    it('stores root partition key', () => {
      const info = new EventRetrievalInfo(
        OptionalValue.fromValue('tenant1'),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );

      expect(info.rootPartitionKey.getValue()).toBe('tenant1');
    });

    it('stores aggregate stream', () => {
      const stream = new AggregateGroupStream('Orders');
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.fromValue(stream),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );

      expect(info.aggregateStream.getValue()).toBe(stream);
    });

    it('stores aggregate id', () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.fromValue('123'),
        SortableIdCondition.none()
      );

      expect(info.aggregateId.getValue()).toBe('123');
    });

    it('stores sortable id condition', () => {
      const condition = SortableIdCondition.since(SortableUniqueId.generate());
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        condition
      );

      expect(info.sortableIdCondition).toBe(condition);
    });

    it('defaults max count to empty', () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );

      expect(info.maxCount.hasValueProperty).toBe(false);
    });

    it('stores max count when provided', () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none(),
        OptionalValue.fromValue(100)
      );

      expect(info.maxCount.getValue()).toBe(100);
    });
  });

  describe('fromNullableValues()', () => {
    it('converts null root partition key to empty optional', () => {
      const stream = new AggregateGroupStream('Orders');
      const info = EventRetrievalInfo.fromNullableValues(
        null,
        stream,
        null,
        SortableIdCondition.none()
      );

      expect(info.rootPartitionKey.hasValueProperty).toBe(false);
    });

    it('converts undefined root partition key to empty optional', () => {
      const stream = new AggregateGroupStream('Orders');
      const info = EventRetrievalInfo.fromNullableValues(
        undefined,
        stream,
        undefined,
        SortableIdCondition.none()
      );

      expect(info.rootPartitionKey.hasValueProperty).toBe(false);
    });

    it('converts string root partition key to optional with value', () => {
      const stream = new AggregateGroupStream('Orders');
      const info = EventRetrievalInfo.fromNullableValues(
        'tenant1',
        stream,
        null,
        SortableIdCondition.none()
      );

      expect(info.rootPartitionKey.getValue()).toBe('tenant1');
    });

    it('uses default root partition when aggregate ID is provided without root partition', () => {
      const stream = new AggregateGroupStream('Orders');
      const info = EventRetrievalInfo.fromNullableValues(
        null,
        stream,
        '123',
        SortableIdCondition.none()
      );

      expect(info.rootPartitionKey.getValue()).toBe('default');
    });

    it('does not use default root partition when neither aggregate ID nor root partition are provided', () => {
      const stream = new AggregateGroupStream('Orders');
      const info = EventRetrievalInfo.fromNullableValues(
        null,
        stream,
        null,
        SortableIdCondition.none()
      );

      expect(info.rootPartitionKey.hasValueProperty).toBe(false);
    });

    it('wraps aggregate stream in optional', () => {
      const stream = new AggregateGroupStream('Orders');
      const info = EventRetrievalInfo.fromNullableValues(
        null,
        stream,
        null,
        SortableIdCondition.none()
      );

      expect(info.aggregateStream.getValue()).toBe(stream);
    });

    it('converts null max count to empty optional', () => {
      const stream = new AggregateGroupStream('Orders');
      const info = EventRetrievalInfo.fromNullableValues(
        null,
        stream,
        null,
        SortableIdCondition.none(),
        null
      );

      expect(info.maxCount.hasValueProperty).toBe(false);
    });

    it('converts number max count to optional with value', () => {
      const stream = new AggregateGroupStream('Orders');
      const info = EventRetrievalInfo.fromNullableValues(
        null,
        stream,
        null,
        SortableIdCondition.none(),
        50
      );

      expect(info.maxCount.getValue()).toBe(50);
    });
  });

  describe('all()', () => {
    it('creates info with all empty optionals', () => {
      const info = EventRetrievalInfo.all();

      expect(info.rootPartitionKey.hasValueProperty).toBe(false);
      expect(info.aggregateStream.hasValueProperty).toBe(false);
      expect(info.aggregateId.hasValueProperty).toBe(false);
      expect(info.maxCount.hasValueProperty).toBe(false);
    });

    it('uses none sortable ID condition', () => {
      const info = EventRetrievalInfo.all();

      expect(info.sortableIdCondition).toBe(SortableIdCondition.none());
    });
  });

  describe('fromPartitionKeys()', () => {
    it('extracts root partition key', () => {
      const keys = PartitionKeys.create('123', 'Orders', 'tenant1');
      const info = EventRetrievalInfo.fromPartitionKeys(keys);

      expect(info.rootPartitionKey.getValue()).toBe('tenant1');
    });

    it('uses default root partition key when not provided', () => {
      const keys = PartitionKeys.create('123', 'Orders');
      const info = EventRetrievalInfo.fromPartitionKeys(keys);

      expect(info.rootPartitionKey.getValue()).toBe('default');
    });

    it('creates aggregate group stream from group', () => {
      const keys = PartitionKeys.create('123', 'Orders');
      const info = EventRetrievalInfo.fromPartitionKeys(keys);

      expect(info.aggregateStream.getValue().getStreamNames()).toEqual(['Orders']);
    });

    it('uses default aggregate group when not provided', () => {
      const keys = PartitionKeys.create('123');
      const info = EventRetrievalInfo.fromPartitionKeys(keys);

      expect(info.aggregateStream.getValue().getStreamNames()).toEqual(['default']);
    });

    it('extracts aggregate ID', () => {
      const keys = PartitionKeys.create('123');
      const info = EventRetrievalInfo.fromPartitionKeys(keys);

      expect(info.aggregateId.getValue()).toBe('123');
    });

    it('uses none sortable ID condition', () => {
      const keys = PartitionKeys.create('123');
      const info = EventRetrievalInfo.fromPartitionKeys(keys);

      expect(info.sortableIdCondition).toBeInstanceOf(SortableIdCondition.none().constructor);
    });
  });

  describe('getIsPartition()', () => {
    it('returns true when aggregate ID is present', () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.fromValue('123'),
        SortableIdCondition.none()
      );

      expect(info.getIsPartition()).toBe(true);
    });

    it('returns false when aggregate ID is not present', () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );

      expect(info.getIsPartition()).toBe(false);
    });
  });

  describe('hasAggregateStream()', () => {
    it('returns true when aggregate stream is present with stream names', () => {
      const stream = new AggregateGroupStream('Orders');
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.fromValue(stream),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );

      expect(info.hasAggregateStream()).toBe(true);
    });

    it('returns false when aggregate stream is not present', () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );

      expect(info.hasAggregateStream()).toBe(false);
    });
  });

  describe('hasRootPartitionKey()', () => {
    it('returns true when root partition key is present', () => {
      const info = new EventRetrievalInfo(
        OptionalValue.fromValue('tenant1'),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );

      expect(info.hasRootPartitionKey()).toBe(true);
    });

    it('returns false when root partition key is not present', () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );

      expect(info.hasRootPartitionKey()).toBe(false);
    });
  });

  describe('getPartitionKey()', () => {
    it('returns partition key string when all required values are present', () => {
      const info = new EventRetrievalInfo(
        OptionalValue.fromValue('tenant1'),
        OptionalValue.fromValue(new AggregateGroupStream('Orders')),
        OptionalValue.fromValue('123'),
        SortableIdCondition.none()
      );

      const result = info.getPartitionKey();
      
      expect(result.isOk()).toBe(true);
      expect(result._unsafeUnwrap()).toBe('tenant1@Orders@123');
    });

    it('returns error when aggregate ID is not set', () => {
      const info = new EventRetrievalInfo(
        OptionalValue.fromValue('tenant1'),
        OptionalValue.fromValue(new AggregateGroupStream('Orders')),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );

      const result = info.getPartitionKey();
      
      expect(result.isErr()).toBe(true);
      expect(result._unsafeUnwrapErr().message).toBe('Partition key is not set');
    });

    it('returns error when aggregate stream is not set', () => {
      const info = new EventRetrievalInfo(
        OptionalValue.fromValue('tenant1'),
        OptionalValue.empty(),
        OptionalValue.fromValue('123'),
        SortableIdCondition.none()
      );

      const result = info.getPartitionKey();
      
      expect(result.isErr()).toBe(true);
      expect(result._unsafeUnwrapErr().message).toBe('Aggregate stream is not set');
    });

    it('returns error when root partition key is not set', () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.fromValue(new AggregateGroupStream('Orders')),
        OptionalValue.fromValue('123'),
        SortableIdCondition.none()
      );

      const result = info.getPartitionKey();
      
      expect(result.isErr()).toBe(true);
      expect(result._unsafeUnwrapErr().message).toBe('Root partition key is not set');
    });
  });
});

describe('AggregateGroupStream', () => {
  it('returns single stream name in array', () => {
    const stream = new AggregateGroupStream('Orders');
    
    expect(stream.getStreamNames()).toEqual(['Orders']);
  });

  it('returns stream name from getSingleStreamName', () => {
    const stream = new AggregateGroupStream('Orders');
    const result = stream.getSingleStreamName();
    
    expect(result.isOk()).toBe(true);
    expect(result._unsafeUnwrap()).toBe('Orders');
  });
});