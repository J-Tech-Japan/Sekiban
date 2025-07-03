import { describe, it, expect, beforeEach, vi } from 'vitest';
import { ActorId, DaprClient } from '@dapr/dapr';
import type { IAggregatePayload, IProjector, EventDocument, PartitionKeys } from '@sekiban/core';
import { SortableUniqueId } from '@sekiban/core';
import { DaprAggregateActor } from './aggregate-actor';
import type { ISnapshotStrategy } from '../snapshot/strategies';
import { CountBasedSnapshotStrategy } from '../snapshot/strategies';
import type { AggregateSnapshot } from '../snapshot/types';
import { ok, err } from 'neverthrow';

// Test payload and projector
interface TestUserPayload extends IAggregatePayload {
  name: string;
  email: string;
  createdAt: Date;
}

class TestUserProjector implements IProjector<TestUserPayload> {
  initialState(): TestUserPayload {
    return {
      name: '',
      email: '',
      createdAt: new Date(),
    };
  }

  applyEvent(state: TestUserPayload, event: EventDocument): TestUserPayload {
    switch (event.payload.eventType) {
      case 'UserCreated':
        return {
          name: event.payload.name,
          email: event.payload.email,
          createdAt: event.createdAt,
        };
      case 'UserNameChanged':
        return {
          ...state,
          name: event.payload.newName,
        };
      case 'UserEmailChanged':
        return {
          ...state,
          email: event.payload.newEmail,
        };
      default:
        return state;
    }
  }
}

// Mock implementations
class MockStateManager {
  private state: Map<string, any> = new Map();

  // ActorStateManager interface methods
  async getState<T>(stateName: string): Promise<T> {
    const value = this.state.get(stateName);
    if (value === undefined) {
      throw new Error(`Actor state with name ${stateName} was not found`);
    }
    return value;
  }

  async tryGetState<T>(stateName: string): Promise<[boolean, T | null]> {
    const value = this.state.get(stateName);
    return [value !== undefined, value ?? null];
  }

  async setState(stateName: string, value: any): Promise<void> {
    this.state.set(stateName, value);
  }

  async removeState(stateName: string): Promise<void> {
    this.state.delete(stateName);
  }

  async containsState(stateName: string): Promise<boolean> {
    return this.state.has(stateName);
  }
  
  // Convenience methods for tests
  async get<T>(key: string): Promise<T | undefined> {
    try {
      return await this.getState<T>(key);
    } catch {
      return undefined;
    }
  }
  
  async set(key: string, value: any): Promise<void> {
    return this.setState(key, value);
  }
}

class MockEventStore {
  private events: EventDocument[] = [];

  async loadEventsSince(aggregateId: string, afterEventId: string | null): Promise<EventDocument[]> {
    const aggregateEvents = this.events.filter(e => e.aggregateId === aggregateId);
    
    if (!afterEventId) {
      return aggregateEvents;
    }
    
    const index = aggregateEvents.findIndex(e => e.id === afterEventId);
    if (index === -1) {
      // If the afterEventId is not found, return all events for the aggregate
      // This handles the case where we have a snapshot but events before it are not in memory
      return aggregateEvents;
    }
    
    return aggregateEvents.slice(index + 1);
  }

  async saveEvents(events: EventDocument[]): Promise<void> {
    this.events.push(...events);
  }

  getEvents(): EventDocument[] {
    return [...this.events];
  }

  clear(): void {
    this.events = [];
  }
}

// Mock DaprClient
class MockDaprClient extends DaprClient {
  constructor() {
    super({
      daprHost: 'localhost',
      daprPort: '3500',
    });
  }
}

// Concrete test actor
class TestAggregateActor extends DaprAggregateActor<TestUserPayload> {
  constructor(
    daprClient: DaprClient,
    actorId: ActorId,
    eventStore: MockEventStore,
    projector: IProjector<TestUserPayload>,
    snapshotStrategy: ISnapshotStrategy,
    private mockStateManager: MockStateManager
  ) {
    super(daprClient, actorId, eventStore as any, projector, snapshotStrategy);
    
    // Replace the state manager after construction
    Object.defineProperty(this, 'stateManager', {
      get: () => this.mockStateManager,
      configurable: true
    });
  }
}

describe('DaprAggregateActor', () => {
  let stateManager: MockStateManager;
  let eventStore: MockEventStore;
  let projector: TestUserProjector;
  let snapshotStrategy: ISnapshotStrategy;
  let actor: TestAggregateActor;
  let daprClient: MockDaprClient;
  let actorId: ActorId;

  beforeEach(() => {
    stateManager = new MockStateManager();
    eventStore = new MockEventStore();
    projector = new TestUserProjector();
    snapshotStrategy = new CountBasedSnapshotStrategy(3); // Snapshot every 3 events
    daprClient = new MockDaprClient();
    actorId = new ActorId('test-actor-123');
    
    actor = new TestAggregateActor(daprClient, actorId, eventStore, projector, snapshotStrategy, stateManager);
  });

  describe('onActivate', () => {
    it('should load snapshot on activation if exists', async () => {
      const snapshot: AggregateSnapshot<TestUserPayload> = {
        aggregateId: 'user-123',
        partitionKey: { aggregateId: 'user-123', group: 'User', rootPartitionKey: 'default' } as PartitionKeys,
        payload: {
          name: 'John Doe',
          email: 'john@example.com',
          createdAt: new Date('2024-01-01'),
        },
        version: 2,
        lastEventId: 'event-2',
        lastEventTimestamp: new Date('2024-01-01T10:00:00Z'),
        snapshotTimestamp: new Date('2024-01-01T10:00:01Z'),
      };

      await stateManager.set('snapshot', snapshot);
      await actor.onActivate();

      const state = await actor.getState();
      expect(state.isOk()).toBe(true);
      if (state.isOk()) {
        expect(state.value.payload).toEqual(snapshot.payload);
        expect(state.value.version).toBe(2);
      }
    });

    it('should start with initial state if no snapshot exists', async () => {
      await actor.onActivate();

      const state = await actor.getState();
      expect(state.isOk()).toBe(true);
      if (state.isOk()) {
        expect(state.value.payload).toEqual(projector.initialState());
        expect(state.value.version).toBe(0);
      }
    });
  });

  describe('getState', () => {
    it('should rebuild from events if no snapshot exists', async () => {
      const events: EventDocument[] = [
        {
          id: 'event-1',
          aggregateId: 'user-123',
          sortableUniqueId: SortableUniqueId.generate(),
          payload: {
            eventType: 'UserCreated',
            name: 'Jane Doe',
            email: 'jane@example.com',
          },
          version: 1,
          createdAt: new Date('2024-01-01T09:00:00Z'),
          metadata: {} as any,
          partitionKeys: { aggregateId: 'user-123', group: 'User', rootPartitionKey: 'default' } as PartitionKeys,
        },
        {
          id: 'event-2',
          aggregateId: 'user-123',
          sortableUniqueId: SortableUniqueId.generate(),
          payload: {
            eventType: 'UserNameChanged',
            newName: 'Jane Smith',
          },
          version: 2,
          createdAt: new Date('2024-01-01T10:00:00Z'),
          metadata: {} as any,
          partitionKeys: { aggregateId: 'user-123', group: 'User', rootPartitionKey: 'default' } as PartitionKeys,
        },
      ];

      await eventStore.saveEvents(events);
      vi.spyOn(actor as any, 'aggregateId', 'get').mockReturnValue('user-123');

      const state = await actor.getState();
      expect(state.isOk()).toBe(true);
      if (state.isOk()) {
        expect(state.value.payload.name).toBe('Jane Smith');
        expect(state.value.payload.email).toBe('jane@example.com');
        expect(state.value.version).toBe(2);
      }
    });

    it('should apply new events on top of snapshot', async () => {
      const snapshot: AggregateSnapshot<TestUserPayload> = {
        aggregateId: 'user-123',
        partitionKey: { aggregateId: 'user-123', group: 'User', rootPartitionKey: 'default' } as PartitionKeys,
        payload: {
          name: 'Initial Name',
          email: 'initial@example.com',
          createdAt: new Date('2024-01-01'),
        },
        version: 1,
        lastEventId: 'event-1',
        lastEventTimestamp: new Date('2024-01-01T09:00:00Z'),
        snapshotTimestamp: new Date('2024-01-01T09:00:01Z'),
      };

      const newEvent: EventDocument = {
        id: 'event-2',
        aggregateId: 'user-123',
        sortableUniqueId: SortableUniqueId.generate(),
        payload: {
          eventType: 'UserEmailChanged',
          newEmail: 'updated@example.com',
        },
        version: 2,
        createdAt: new Date('2024-01-01T10:00:00Z'),
        metadata: {} as any,
        partitionKeys: { aggregateId: 'user-123', group: 'User', rootPartitionKey: 'default' } as PartitionKeys,
      };

      await stateManager.set('snapshot', snapshot);
      await eventStore.saveEvents([newEvent]);
      vi.spyOn(actor as any, 'aggregateId', 'get').mockReturnValue('user-123');
      
      await actor.onActivate();
      const state = await actor.getState();
      
      expect(state.isOk()).toBe(true);
      if (state.isOk()) {
        expect(state.value.payload.name).toBe('Initial Name');
        expect(state.value.payload.email).toBe('updated@example.com');
        expect(state.value.version).toBe(2);
      }
    });
  });

  describe('applyEvents', () => {
    it('should apply events and create snapshot when strategy triggers', async () => {
      vi.spyOn(actor as any, 'aggregateId', 'get').mockReturnValue('user-123');
      await actor.onActivate();

      const events: EventDocument[] = [
        {
          id: 'event-1',
          aggregateId: 'user-123',
          sortableUniqueId: SortableUniqueId.generate(),
          payload: {
            eventType: 'UserCreated',
            name: 'Test User',
            email: 'test@example.com',
          },
          version: 1,
          createdAt: new Date(),
          metadata: {} as any,
          partitionKeys: { aggregateId: 'user-123', group: 'User', rootPartitionKey: 'default' } as PartitionKeys,
        },
        {
          id: 'event-2',
          aggregateId: 'user-123',
          sortableUniqueId: SortableUniqueId.generate(),
          payload: {
            eventType: 'UserNameChanged',
            newName: 'Updated Name',
          },
          version: 2,
          createdAt: new Date(),
          metadata: {} as any,
          partitionKeys: { aggregateId: 'user-123', group: 'User', rootPartitionKey: 'default' } as PartitionKeys,
        },
        {
          id: 'event-3',
          aggregateId: 'user-123',
          sortableUniqueId: SortableUniqueId.generate(),
          payload: {
            eventType: 'UserEmailChanged',
            newEmail: 'new@example.com',
          },
          version: 3,
          createdAt: new Date(),
          metadata: {} as any,
          partitionKeys: { aggregateId: 'user-123', group: 'User', rootPartitionKey: 'default' } as PartitionKeys,
        },
      ];

      // Save events to the store first
      await eventStore.saveEvents(events);
      
      const result = await actor.applyEvents(events);
      expect(result.isOk()).toBe(true);

      // Check that snapshot was created (strategy triggers at 3 events)
      const snapshot = await stateManager.get<AggregateSnapshot<TestUserPayload>>('snapshot');
      expect(snapshot).toBeDefined();
      expect(snapshot?.version).toBe(3);
      expect(snapshot?.payload.name).toBe('Updated Name');
      expect(snapshot?.payload.email).toBe('new@example.com');
      expect(snapshot?.lastEventId).toBe('event-3');
    });

    it('should not create snapshot when strategy does not trigger', async () => {
      vi.spyOn(actor as any, 'aggregateId', 'get').mockReturnValue('user-123');
      await actor.onActivate();

      const events: EventDocument[] = [
        {
          id: 'event-1',
          aggregateId: 'user-123',
          sortableUniqueId: SortableUniqueId.generate(),
          payload: {
            eventType: 'UserCreated',
            name: 'Test User',
            email: 'test@example.com',
          },
          version: 1,
          createdAt: new Date(),
          metadata: {} as any,
          partitionKeys: { aggregateId: 'user-123', group: 'User', rootPartitionKey: 'default' } as PartitionKeys,
        },
      ];

      const result = await actor.applyEvents(events);
      expect(result.isOk()).toBe(true);

      // Check that no snapshot was created (need 3 events)
      const snapshot = await stateManager.get<AggregateSnapshot<TestUserPayload>>('snapshot');
      expect(snapshot).toBeUndefined();
    });
  });

  describe('createSnapshot', () => {
    it('should create and persist snapshot', async () => {
      vi.spyOn(actor as any, 'aggregateId', 'get').mockReturnValue('user-123');
      
      const events: EventDocument[] = [
        {
          id: 'event-1',
          aggregateId: 'user-123',
          sortableUniqueId: SortableUniqueId.generate(),
          payload: {
            eventType: 'UserCreated',
            name: 'Snapshot Test',
            email: 'snapshot@example.com',
          },
          version: 1,
          createdAt: new Date('2024-01-01T12:00:00Z'),
          metadata: {} as any,
          partitionKeys: { aggregateId: 'user-123', group: 'User', rootPartitionKey: 'default' } as PartitionKeys,
        },
      ];

      await eventStore.saveEvents(events);
      await actor.onActivate();

      const result = await actor.createSnapshot();
      expect(result.isOk()).toBe(true);

      const snapshot = await stateManager.get<AggregateSnapshot<TestUserPayload>>('snapshot');
      expect(snapshot).toBeDefined();
      expect(snapshot?.payload.name).toBe('Snapshot Test');
      expect(snapshot?.payload.email).toBe('snapshot@example.com');
      expect(snapshot?.version).toBe(1);
      expect(snapshot?.lastEventId).toBe('event-1');
      expect(snapshot?.snapshotTimestamp).toBeInstanceOf(Date);
    });

    it('should handle snapshot creation errors', async () => {
      vi.spyOn(actor as any, 'aggregateId', 'get').mockReturnValue('user-123');
      
      // Add an event so there's something to snapshot
      const event: EventDocument = {
        id: 'event-1',
        aggregateId: 'user-123',
        sortableUniqueId: SortableUniqueId.generate(),
        payload: {
          eventType: 'UserCreated',
          name: 'Test User',
          email: 'test@example.com',
        },
        version: 1,
        createdAt: new Date(),
        metadata: {} as any,
        partitionKeys: { aggregateId: 'user-123', group: 'User', rootPartitionKey: 'default' } as PartitionKeys,
      };
      await eventStore.saveEvents([event]);
      
      vi.spyOn(stateManager, 'setState').mockRejectedValueOnce(new Error('Storage error'));

      await actor.onActivate();
      const result = await actor.createSnapshot();
      
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error.message).toContain('Failed to save snapshot');
      }
    });
  });

  describe('error handling', () => {
    it('should handle event loading errors', async () => {
      vi.spyOn(actor as any, 'aggregateId', 'get').mockReturnValue('user-123');
      vi.spyOn(eventStore, 'loadEventsSince').mockRejectedValueOnce(new Error('Database error'));

      const state = await actor.getState();
      expect(state.isErr()).toBe(true);
      if (state.isErr()) {
        expect(state.error.message).toContain('Failed to load events');
      }
    });

    it('should handle corrupted snapshot data', async () => {
      vi.spyOn(actor as any, 'aggregateId', 'get').mockReturnValue('user-123');
      
      // Store invalid snapshot data
      await stateManager.set('snapshot', { invalid: 'data' });
      
      await actor.onActivate();
      const state = await actor.getState();
      
      // Should fall back to rebuilding from events
      expect(state.isOk()).toBe(true);
      if (state.isOk()) {
        expect(state.value.payload.name).toBe('');
        expect(state.value.payload.email).toBe('');
        expect(state.value.payload.createdAt).toBeInstanceOf(Date);
      }
    });
  });
});