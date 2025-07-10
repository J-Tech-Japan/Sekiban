import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { PostgresEventStore } from '../postgres-event-store.js';
import { 
  EventRetrievalInfo, 
  OptionalValue, 
  SortableIdCondition, 
  AggregateGroupStream, 
  IEvent, 
  createEvent,
  PartitionKeys,
  StorageError
} from '@sekiban/core';
import { Pool } from 'pg';

// Mock pg
vi.mock('pg', () => {
  const mockClient = {
    query: vi.fn(),
    release: vi.fn()
  };

  const mockPool = {
    connect: vi.fn().mockResolvedValue(mockClient),
    query: vi.fn().mockResolvedValue({ rows: [], rowCount: 0 }),
    end: vi.fn().mockResolvedValue(undefined)
  };

  return {
    Pool: vi.fn().mockImplementation(() => mockPool)
  };
});

describe('PostgresEventStore', () => {
  let pool: Pool;
  let store: PostgresEventStore;
  let mockClient: any;

  beforeEach(async () => {
    vi.clearAllMocks();
    
    pool = new Pool({
      connectionString: 'postgresql://test:test@localhost:5432/test'
    });
    
    mockClient = {
      query: vi.fn(),
      release: vi.fn()
    };
    
    vi.mocked(pool.connect).mockResolvedValue(mockClient);
    vi.mocked(pool.query).mockResolvedValue({ rows: [], rowCount: 0 } as any);
    
    store = new PostgresEventStore(pool);
    await store.initialize();
  });

  afterEach(async () => {
    await store.close();
  });

  describe('initialize()', () => {
    it('creates events table if not exists', async () => {
      expect(pool.query).toHaveBeenCalledWith(
        expect.stringContaining('CREATE TABLE IF NOT EXISTS events')
      );
    });

    it('creates indexes on events table', async () => {
      expect(pool.query).toHaveBeenCalledWith(
        expect.stringContaining('CREATE INDEX IF NOT EXISTS')
      );
    });

    it('returns success on successful initialization', async () => {
      const newStore = new PostgresEventStore(pool);
      const result = await newStore.initialize();

      expect(result.isOk()).toBe(true);
    });

    it('returns error on initialization failure', async () => {
      const error = new Error('Table creation failed');
      vi.mocked(pool.query).mockRejectedValueOnce(error);

      const newStore = new PostgresEventStore(pool);
      const result = await newStore.initialize();

      expect(result.isErr()).toBe(true);
      expect(result._unsafeUnwrapErr().message).toContain('Failed to initialize PostgreSQL');
    });
  });

  describe('saveEvents()', () => {
    it('saves a single event', async () => {
      const event = createEvent({
        partitionKeys: PartitionKeys.create('123', 'Orders'),
        aggregateType: 'Order',
        eventType: 'OrderCreated',
        payload: { orderId: '123' },
        version: 1
      });

      vi.mocked(mockClient.query).mockResolvedValueOnce({ rows: [], rowCount: 1 });

      await store.saveEvents([event]);

      expect(mockClient.query).toHaveBeenCalledWith(
        expect.stringContaining('INSERT INTO events'),
        expect.arrayContaining([
          event.id.toString(),
          event.partitionKeys.partitionKey,
          expect.any(String), // JSON stringified event
          event.partitionKeys.rootPartitionKey || 'default',
          event.partitionKeys.group || 'default',
          event.partitionKeys.aggregateId,
          event.aggregateType,
          event.eventType,
          event.version
        ])
      );
    });

    it('saves multiple events in a transaction', async () => {
      const events = [
        createEvent({
          partitionKeys: PartitionKeys.create('123', 'Orders'),
          aggregateType: 'Order',
          eventType: 'OrderCreated',
          payload: { orderId: '123' },
          version: 1
        }),
        createEvent({
          partitionKeys: PartitionKeys.create('123', 'Orders'),
          aggregateType: 'Order',
          eventType: 'OrderUpdated',
          payload: { orderId: '123' },
          version: 2
        })
      ];

      vi.mocked(mockClient.query).mockResolvedValue({ rows: [], rowCount: 1 });

      await store.saveEvents(events);

      expect(mockClient.query).toHaveBeenCalledWith('BEGIN');
      expect(mockClient.query).toHaveBeenCalledWith('COMMIT');
      expect(mockClient.query).toHaveBeenCalledTimes(4); // BEGIN + 2 INSERTs + COMMIT
    });

    it('rolls back transaction on save failure', async () => {
      const event = createEvent({
        partitionKeys: PartitionKeys.create('123', 'Orders'),
        aggregateType: 'Order',
        eventType: 'OrderCreated',
        payload: { orderId: '123' },
        version: 1
      });

      vi.mocked(mockClient.query)
        .mockResolvedValueOnce({ rows: [], rowCount: 0 }) // BEGIN
        .mockRejectedValueOnce(new Error('Insert failed')); // INSERT

      await expect(store.saveEvents([event])).rejects.toThrow('Failed to save events');
      expect(mockClient.query).toHaveBeenCalledWith('ROLLBACK');
    });

    it('throws error when pool connection fails', async () => {
      const event = createEvent({
        partitionKeys: PartitionKeys.create('123', 'Orders'),
        aggregateType: 'Order',
        eventType: 'OrderCreated',
        payload: { orderId: '123' },
        version: 1
      });

      vi.mocked(pool.connect).mockRejectedValueOnce(new Error('Connection failed'));

      await expect(store.saveEvents([event])).rejects.toThrow('Failed to save events');
    });
  });

  describe('getEvents()', () => {
    it('returns all events when no filters specified', async () => {
      const mockEvents = [
        {
          id: 'event-1',
          data: JSON.stringify(createEvent({
            partitionKeys: PartitionKeys.create('1', 'Orders'),
            aggregateType: 'Order',
            eventType: 'Event1',
            payload: {},
            version: 1
          }))
        },
        {
          id: 'event-2',
          data: JSON.stringify(createEvent({
            partitionKeys: PartitionKeys.create('2', 'Orders'),
            aggregateType: 'Order',
            eventType: 'Event2',
            payload: {},
            version: 1
          }))
        }
      ];

      vi.mocked(pool.query).mockResolvedValueOnce({ 
        rows: mockEvents, 
        rowCount: mockEvents.length 
      } as any);

      const info = EventRetrievalInfo.all();
      const result = await store.getEvents(info);

      expect(result.isOk()).toBe(true);
      expect(result._unsafeUnwrap()).toHaveLength(2);
      expect(pool.query).toHaveBeenCalledWith(
        expect.stringContaining('SELECT data FROM events'),
        []
      );
    });

    it('filters by root partition key', async () => {
      const mockEvents = [
        {
          data: JSON.stringify(createEvent({
            partitionKeys: PartitionKeys.create('1', 'Orders', 'tenant1'),
            aggregateType: 'Order',
            eventType: 'Event1',
            payload: {},
            version: 1
          }))
        }
      ];

      vi.mocked(pool.query).mockResolvedValueOnce({ 
        rows: mockEvents, 
        rowCount: mockEvents.length 
      } as any);

      const info = new EventRetrievalInfo(
        OptionalValue.fromValue('tenant1'),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );
      const result = await store.getEvents(info);

      expect(pool.query).toHaveBeenCalledWith(
        expect.stringContaining('WHERE root_partition_key = $1'),
        ['tenant1']
      );
    });

    it('filters by aggregate stream', async () => {
      const mockEvents = [
        {
          data: JSON.stringify(createEvent({
            partitionKeys: PartitionKeys.create('1', 'Orders'),
            aggregateType: 'Order',
            eventType: 'Event1',
            payload: {},
            version: 1
          }))
        }
      ];

      vi.mocked(pool.query).mockResolvedValueOnce({ 
        rows: mockEvents, 
        rowCount: mockEvents.length 
      } as any);

      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.fromValue(new AggregateGroupStream('Orders')),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );
      const result = await store.getEvents(info);

      expect(pool.query).toHaveBeenCalledWith(
        expect.stringContaining('aggregate_group = $'),
        ['Orders']
      );
    });

    it('filters by aggregate ID', async () => {
      const mockEvents = [
        {
          data: JSON.stringify(createEvent({
            partitionKeys: PartitionKeys.create('123', 'Orders'),
            aggregateType: 'Order',
            eventType: 'Event1',
            payload: {},
            version: 1
          }))
        }
      ];

      vi.mocked(pool.query).mockResolvedValueOnce({ 
        rows: mockEvents, 
        rowCount: mockEvents.length 
      } as any);

      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.fromValue('123'),
        SortableIdCondition.none()
      );
      const result = await store.getEvents(info);

      expect(pool.query).toHaveBeenCalledWith(
        expect.stringContaining('aggregate_id = $'),
        ['123']
      );
    });

    it('applies max count limit', async () => {
      const mockEvents = Array.from({ length: 3 }, (_, i) => ({
        data: JSON.stringify(createEvent({
          partitionKeys: PartitionKeys.create(String(i), 'Orders'),
          aggregateType: 'Order',
          eventType: `Event${i}`,
          payload: {},
          version: 1
        }))
      }));

      vi.mocked(pool.query).mockResolvedValueOnce({ 
        rows: mockEvents, 
        rowCount: mockEvents.length 
      } as any);

      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none(),
        OptionalValue.fromValue(3)
      );
      const result = await store.getEvents(info);

      expect(pool.query).toHaveBeenCalledWith(
        expect.stringContaining('LIMIT $'),
        [3]
      );
    });

    it('applies sortable ID conditions after query', async () => {
      const event1 = createEvent({
        partitionKeys: PartitionKeys.create('1', 'Orders'),
        aggregateType: 'Order',
        eventType: 'Event1',
        payload: {},
        version: 1
      });
      const event2 = createEvent({
        partitionKeys: PartitionKeys.create('1', 'Orders'),
        aggregateType: 'Order',
        eventType: 'Event2',
        payload: {},
        version: 2
      });

      const mockEvents = [
        { data: JSON.stringify(event1) },
        { data: JSON.stringify(event2) }
      ];

      vi.mocked(pool.query).mockResolvedValueOnce({ 
        rows: mockEvents, 
        rowCount: mockEvents.length 
      } as any);

      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.since(event1.id)
      );
      const result = await store.getEvents(info);

      expect(result.isOk()).toBe(true);
      expect(result._unsafeUnwrap()).toHaveLength(1);
      expect(result._unsafeUnwrap()[0]?.eventType).toBe('Event2');
    });

    it('returns error on query failure', async () => {
      const error = new Error('Query failed');
      vi.mocked(pool.query).mockRejectedValueOnce(error);

      const info = EventRetrievalInfo.all();
      const result = await store.getEvents(info);

      expect(result.isErr()).toBe(true);
      expect(result._unsafeUnwrapErr().message).toContain('Failed to query events');
    });
  });

  describe('close()', () => {
    it('ends the pool connection', async () => {
      const result = await store.close();

      expect(result.isOk()).toBe(true);
      expect(pool.end).toHaveBeenCalled();
    });

    it('returns error on close failure', async () => {
      const error = new Error('Close failed');
      vi.mocked(pool.end).mockRejectedValueOnce(error);

      const result = await store.close();

      expect(result.isErr()).toBe(true);
      expect(result._unsafeUnwrapErr().message).toContain('Failed to close PostgreSQL');
    });
  });
});