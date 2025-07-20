import { describe, it, expect, beforeEach } from 'vitest';
import { InMemoryEventStore } from '../../storage/in-memory-event-store';
import { StorageProviderType } from '../../storage/storage-provider';
import { EventRetrievalInfo, OptionalValue, AggregateGroupStream, SortableIdCondition } from '../../events/event-retrieval-info';
import { PartitionKeys } from '../../documents/partition-keys';
import { createEvent } from '../../events/event';
import { IEvent } from '../../events/event';

describe('InMemoryEventStore', () => {
  let store: InMemoryEventStore;

  beforeEach(() => {
    store = new InMemoryEventStore({ type: StorageProviderType.InMemory });
  });

  describe('saveEvents()', () => {
    it('saves a single event', async () => {
      const event = createEvent({
        partitionKeys: PartitionKeys.create('1', 'Orders'),
        eventType: 'OrderCreated',
        payload: { orderId: '1' },
        version: 1
      });

      await store.saveEvents([event]);

      const result = await store.getEvents(EventRetrievalInfo.all());
      expect(result._unsafeUnwrap().length).toBe(1);
    });

    it('saves multiple events', async () => {
      const events = [
        createEvent({
          partitionKeys: PartitionKeys.create('1', 'Orders'),
          eventType: 'OrderCreated',
          payload: { orderId: '1' },
          version: 1
        }),
        createEvent({
          partitionKeys: PartitionKeys.create('2', 'Orders'),
          eventType: 'OrderCreated',
          payload: { orderId: '2' },
          version: 1
        })
      ];

      await store.saveEvents(events);

      const result = await store.getEvents(EventRetrievalInfo.all());
      expect(result._unsafeUnwrap().length).toBe(2);
    });

    it('preserves event order', async () => {
      const event1 = createEvent({
        partitionKeys: PartitionKeys.create('1', 'Orders'),
        eventType: 'Event1',
        payload: {},
        version: 1
      });
      const event2 = createEvent({
        partitionKeys: PartitionKeys.create('1', 'Orders'),
        eventType: 'Event2',
        payload: {},
        version: 2
      });

      await store.saveEvents([event1, event2]);

      const result = await store.getEvents(EventRetrievalInfo.all());
      const events = result._unsafeUnwrap();
      expect(events[0]?.eventType).toBe('Event1');
      expect(events[1]?.eventType).toBe('Event2');
    });
  });

  describe('getEvents() - filtering by root partition key', () => {
    beforeEach(async () => {
      const events = [
        createEvent({
          partitionKeys: PartitionKeys.create('1', 'Orders', 'tenant1'),
          eventType: 'Event1',
          payload: {},
          version: 1
        }),
        createEvent({
          partitionKeys: PartitionKeys.create('2', 'Orders', 'tenant2'),
          eventType: 'Event2',
          payload: {},
          version: 1
        })
      ];
      await store.saveEvents(events);
    });

    it('returns all events when root partition key is not specified', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(2);
    });

    it('filters events by root partition key', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.fromValue('tenant1'),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(1);
      expect(result._unsafeUnwrap()[0]?.partitionKeys.rootPartitionKey).toBe('tenant1');
    });

    it('returns empty array when no events match root partition key', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.fromValue('tenant3'),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(0);
    });
  });

  describe('getEvents() - filtering by aggregate stream', () => {
    beforeEach(async () => {
      const events = [
        createEvent({
          partitionKeys: PartitionKeys.create('1', 'Orders'),
          eventType: 'OrderCreated',
          payload: {},
          version: 1
        }),
        createEvent({
          partitionKeys: PartitionKeys.create('2', 'Users'),
          eventType: 'UserCreated',
          payload: {},
          version: 1
        })
      ];
      await store.saveEvents(events);
    });

    it('returns all events when aggregate stream is not specified', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(2);
    });

    it('filters events by aggregate stream', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.fromValue(new AggregateGroupStream('Orders')),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(1);
      expect(result._unsafeUnwrap()[0]?.partitionKeys.group).toBe('Orders');
    });

    it('returns empty array when no events match aggregate stream', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.fromValue(new AggregateGroupStream('Products')),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(0);
    });
  });

  describe('getEvents() - filtering by aggregate ID', () => {
    beforeEach(async () => {
      const events = [
        createEvent({
          partitionKeys: PartitionKeys.create('1', 'Orders'),
          eventType: 'Event1',
          payload: {},
          version: 1
        }),
        createEvent({
          partitionKeys: PartitionKeys.create('2', 'Orders'),
          eventType: 'Event2',
          payload: {},
          version: 1
        })
      ];
      await store.saveEvents(events);
    });

    it('returns all events when aggregate ID is not specified', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(2);
    });

    it('filters events by aggregate ID', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.fromValue('1'),
        SortableIdCondition.none()
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(1);
      expect(result._unsafeUnwrap()[0]?.partitionKeys.aggregateId).toBe('1');
    });

    it('returns empty array when no events match aggregate ID', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.fromValue('3'),
        SortableIdCondition.none()
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(0);
    });
  });

  describe('getEvents() - sortable ID conditions', () => {
    let event1: IEvent;
    let event2: IEvent;
    let event3: IEvent;

    beforeEach(async () => {
      event1 = createEvent({
        partitionKeys: PartitionKeys.create('1', 'Orders'),
        eventType: 'Event1',
        payload: {},
        version: 1
      });
      // Small delay to ensure different timestamps
      await new Promise(resolve => setTimeout(resolve, 10));
      event2 = createEvent({
        partitionKeys: PartitionKeys.create('1', 'Orders'),
        eventType: 'Event2',
        payload: {},
        version: 2
      });
      await new Promise(resolve => setTimeout(resolve, 10));
      event3 = createEvent({
        partitionKeys: PartitionKeys.create('1', 'Orders'),
        eventType: 'Event3',
        payload: {},
        version: 3
      });

      await store.saveEvents([event1, event2, event3]);
    });

    it('returns all events with none condition', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(3);
    });

    it('filters events after specified ID with since condition', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.since(event1.id)
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(2);
      expect(result._unsafeUnwrap()[0]?.eventType).toBe('Event2');
    });

    it('returns empty array when all events are before since condition', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.since(event3.id)
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(0);
    });

    it('filters events within range with between condition', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.between(event1.id, event3.id)
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(3);
    });

    it('includes only middle event when between excludes endpoints', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.between(event1.id, event2.id)
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(2);
      expect(result._unsafeUnwrap().some(e => e.eventType === 'Event1')).toBe(true);
      expect(result._unsafeUnwrap().some(e => e.eventType === 'Event2')).toBe(true);
      expect(result._unsafeUnwrap().some(e => e.eventType === 'Event3')).toBe(false);
    });
  });

  describe('getEvents() - max count', () => {
    beforeEach(async () => {
      const events = Array.from({ length: 10 }, (_, i) =>
        createEvent({
          partitionKeys: PartitionKeys.create(String(i), 'Orders'),
          eventType: `Event${i}`,
          payload: {},
          version: 1
        })
      );
      await store.saveEvents(events);
    });

    it('returns all events when max count is not specified', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(10);
    });

    it('limits results to max count', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none(),
        OptionalValue.fromValue(5)
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(5);
    });

    it('returns all events when max count exceeds total', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none(),
        OptionalValue.fromValue(20)
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(10);
    });

    it('returns empty array when max count is zero', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none(),
        OptionalValue.fromValue(0)
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(0);
    });
  });

  describe('getEvents() - combined filters', () => {
    beforeEach(async () => {
      const events = [
        createEvent({
          partitionKeys: PartitionKeys.create('1', 'Orders', 'tenant1'),
          eventType: 'Event1',
          payload: {},
          version: 1
        }),
        createEvent({
          partitionKeys: PartitionKeys.create('1', 'Orders', 'tenant1'),
          eventType: 'Event2',
          payload: {},
          version: 2
        }),
        createEvent({
          partitionKeys: PartitionKeys.create('2', 'Orders', 'tenant1'),
          eventType: 'Event3',
          payload: {},
          version: 1
        }),
        createEvent({
          partitionKeys: PartitionKeys.create('1', 'Users', 'tenant1'),
          eventType: 'Event4',
          payload: {},
          version: 1
        }),
        createEvent({
          partitionKeys: PartitionKeys.create('1', 'Orders', 'tenant2'),
          eventType: 'Event5',
          payload: {},
          version: 1
        })
      ];
      await store.saveEvents(events);
    });

    it('filters by root partition and aggregate stream', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.fromValue('tenant1'),
        OptionalValue.fromValue(new AggregateGroupStream('Orders')),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(3);
    });

    it('filters by all three partition components', async () => {
      const info = new EventRetrievalInfo(
        OptionalValue.fromValue('tenant1'),
        OptionalValue.fromValue(new AggregateGroupStream('Orders')),
        OptionalValue.fromValue('1'),
        SortableIdCondition.none()
      );

      const result = await store.getEvents(info);
      expect(result._unsafeUnwrap().length).toBe(2);
      expect(result._unsafeUnwrap().every(e => 
        e.partitionKeys.rootPartitionKey === 'tenant1' &&
        e.partitionKeys.group === 'Orders' &&
        e.partitionKeys.aggregateId === '1'
      )).toBe(true);
    });
  });
});