import { describe, it, expect, beforeEach, vi } from 'vitest';
import { CosmosEventStore } from '../cosmos-event-store.js';
import { 
  EventRetrievalInfo, 
  OptionalValue, 
  SortableIdCondition, 
  AggregateGroupStream, 
  IEvent, 
  createEvent,
  PartitionKeys,
  StorageError,
  ConnectionError
} from '@sekiban/core';
import { Database, Container } from '@azure/cosmos';

// Mock CosmosDB
vi.mock('@azure/cosmos', () => {
  const mockContainer = {
    items: {
      query: vi.fn(),
      create: vi.fn()
    },
    item: vi.fn(),
    read: vi.fn()
  };

  const mockDatabase = {
    containers: {
      createIfNotExists: vi.fn().mockResolvedValue({ container: mockContainer })
    },
    container: vi.fn().mockReturnValue(mockContainer)
  };

  return {
    Database: vi.fn().mockImplementation(() => mockDatabase),
    Container: vi.fn().mockImplementation(() => mockContainer),
    CosmosClient: vi.fn().mockImplementation(() => ({
      databases: {
        createIfNotExists: vi.fn().mockResolvedValue({ database: mockDatabase })
      }
    }))
  };
});

describe('CosmosEventStore', () => {
  let database: Database;
  let container: Container;
  let store: CosmosEventStore;

  beforeEach(async () => {
    vi.clearAllMocks();
    
    container = {
      items: {
        query: vi.fn(),
        create: vi.fn()
      },
      item: vi.fn(),
      read: vi.fn()
    } as any;

    database = {
      containers: {
        createIfNotExists: vi.fn().mockResolvedValue({ container })
      },
      container: vi.fn().mockReturnValue(container)
    } as any;

    store = new CosmosEventStore(database);
    // Initialize the store
    await store.initialize();
  });

  describe('initialize()', () => {
    it('creates events container if not exists', async () => {
      // Already initialized in beforeEach
      expect(database.containers.createIfNotExists).toHaveBeenCalledWith({
        id: 'events',
        partitionKey: { paths: ['/partitionKey'] }
      });
    });

    it('returns success on successful initialization', async () => {
      // Create a new store to test initialization
      const newStore = new CosmosEventStore(database);
      const result = await newStore.initialize();

      expect(result.isOk()).toBe(true);
    });

    it('returns error on initialization failure', async () => {
      const error = new Error('Container creation failed');
      vi.mocked(database.containers.createIfNotExists).mockRejectedValueOnce(error);

      // Create a new store to test initialization failure
      const newStore = new CosmosEventStore(database);
      const result = await newStore.initialize();

      expect(result.isErr()).toBe(true);
      expect(result._unsafeUnwrapErr().message).toContain('Failed to initialize CosmosDB');
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

      vi.mocked(container.items.create).mockResolvedValueOnce({ resource: event });

      await store.saveEvents([event]);

      expect(container.items.create).toHaveBeenCalledWith({
        ...event,
        partitionKey: 'default-Orders-123'
      });
    });

    it('saves multiple events', async () => {
      const events = [
        createEvent({
          partitionKeys: PartitionKeys.create('123', 'Orders'),
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

      vi.mocked(container.items.create).mockResolvedValue({ resource: {} });

      await store.saveEvents(events);

      expect(container.items.create).toHaveBeenCalledTimes(2);
    });

    it('throws error on save failure', async () => {
      const event = createEvent({
        partitionKeys: PartitionKeys.create('123', 'Orders'),
        aggregateType: 'Order',
        eventType: 'OrderCreated',
        payload: { orderId: '123' },
        version: 1
      });

      const error = new Error('Save failed');
      vi.mocked(container.items.create).mockRejectedValueOnce(error);

      await expect(store.saveEvents([event])).rejects.toThrow('Failed to save events');
    });
  });

  describe('getEvents()', () => {
    it('returns all events when no filters specified', async () => {
      const mockEvents = [
        createEvent({
          partitionKeys: PartitionKeys.create('1', 'Orders'),
          aggregateType: 'Order',
          eventType: 'Event1',
          payload: {},
          version: 1
        }),
        createEvent({
          partitionKeys: PartitionKeys.create('2', 'Orders'),
          aggregateType: 'Order',
          eventType: 'Event2',
          payload: {},
          version: 1
        })
      ];

      const mockIterator = {
        fetchAll: vi.fn().mockResolvedValue({ resources: mockEvents })
      };
      vi.mocked(container.items.query).mockReturnValue(mockIterator as any);

      const info = EventRetrievalInfo.all();
      const result = await store.getEvents(info);

      expect(result.isOk()).toBe(true);
      expect(result._unsafeUnwrap()).toHaveLength(2);
    });

    it('filters by root partition key', async () => {
      const mockEvents = [
        createEvent({
          partitionKeys: PartitionKeys.create('1', 'Orders', 'tenant1'),
          aggregateType: 'Order',
          eventType: 'Event1',
          payload: {},
          version: 1
        })
      ];

      const mockIterator = {
        fetchAll: vi.fn().mockResolvedValue({ resources: mockEvents })
      };
      vi.mocked(container.items.query).mockReturnValue(mockIterator as any);

      const info = new EventRetrievalInfo(
        OptionalValue.fromValue('tenant1'),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );
      const result = await store.getEvents(info);

      expect(container.items.query).toHaveBeenCalledWith(
        expect.objectContaining({
          query: expect.stringContaining('WHERE c.partitionKeys.rootPartitionKey = @rootPartitionKey')
        })
      );
    });

    it('filters by aggregate stream', async () => {
      const mockEvents = [
        createEvent({
          partitionKeys: PartitionKeys.create('1', 'Orders'),
          aggregateType: 'Order',
          eventType: 'Event1',
          payload: {},
          version: 1
        })
      ];

      const mockIterator = {
        fetchAll: vi.fn().mockResolvedValue({ resources: mockEvents })
      };
      vi.mocked(container.items.query).mockReturnValue(mockIterator as any);

      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.fromValue(new AggregateGroupStream('Orders')),
        OptionalValue.empty(),
        SortableIdCondition.none()
      );
      const result = await store.getEvents(info);

      expect(container.items.query).toHaveBeenCalledWith(
        expect.objectContaining({
          query: expect.stringContaining('c.partitionKeys.group IN')
        })
      );
    });

    it('filters by aggregate ID', async () => {
      const mockEvents = [
        createEvent({
          partitionKeys: PartitionKeys.create('123', 'Orders'),
          aggregateType: 'Order',
          eventType: 'Event1',
          payload: {},
          version: 1
        })
      ];

      const mockIterator = {
        fetchAll: vi.fn().mockResolvedValue({ resources: mockEvents })
      };
      vi.mocked(container.items.query).mockReturnValue(mockIterator as any);

      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.fromValue('123'),
        SortableIdCondition.none()
      );
      const result = await store.getEvents(info);

      expect(container.items.query).toHaveBeenCalledWith(
        expect.objectContaining({
          query: expect.stringContaining('WHERE c.partitionKeys.aggregateId = @aggregateId')
        })
      );
    });

    it('applies max count limit', async () => {
      const mockEvents = Array.from({ length: 5 }, (_, i) =>
        createEvent({
          partitionKeys: PartitionKeys.create(String(i), 'Orders'),
          aggregateType: 'Order',
          eventType: `Event${i}`,
          payload: {},
          version: 1
        })
      );

      const mockIterator = {
        fetchAll: vi.fn().mockResolvedValue({ resources: mockEvents.slice(0, 3) })
      };
      vi.mocked(container.items.query).mockReturnValue(mockIterator as any);

      const info = new EventRetrievalInfo(
        OptionalValue.empty(),
        OptionalValue.empty(),
        OptionalValue.empty(),
        SortableIdCondition.none(),
        OptionalValue.fromValue(3)
      );
      const result = await store.getEvents(info);

      expect(container.items.query).toHaveBeenCalledWith(
        expect.objectContaining({
          query: expect.stringContaining('TOP 3')
        })
      );
    });

    it('returns error on query failure', async () => {
      const error = new Error('Query failed');
      vi.mocked(container.items.query).mockImplementation(() => {
        throw error;
      });

      const info = EventRetrievalInfo.all();
      const result = await store.getEvents(info);

      expect(result.isErr()).toBe(true);
      expect(result._unsafeUnwrapErr().message).toContain('Failed to query events');
    });
  });

  describe('close()', () => {
    it('returns success', async () => {
      const result = await store.close();

      expect(result.isOk()).toBe(true);
    });
  });
});