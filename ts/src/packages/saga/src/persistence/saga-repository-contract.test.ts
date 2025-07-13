import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { SagaRepository, SagaSnapshot, SagaConcurrencyError } from './saga-repository';

// This file exports contract tests - actual tests are in all-repositories.test.ts
describe.skip('Contract Test Suite Export', () => {
  it('exports createSagaRepositoryContractTests', () => {
    expect(createSagaRepositoryContractTests).toBeDefined();
  });
});

// Test saga state
interface TestSagaState {
  orderId: string;
  step: number;
  amount: number;
  completed: boolean;
}

/**
 * Contract tests that should pass for all SagaRepository implementations
 * This ensures all adapters behave consistently
 */
export function createSagaRepositoryContractTests(
  createRepository: () => Promise<SagaRepository<TestSagaState>>,
  cleanupRepository?: (repo: SagaRepository<TestSagaState>) => Promise<void>
) {
  return () => {
    let repository: SagaRepository<TestSagaState>;

    beforeEach(async () => {
      repository = await createRepository();
    });

    afterEach(async () => {
      if (cleanupRepository) {
        await cleanupRepository(repository);
      }
    });

    describe('Basic Operations', () => {
      it('should return null when loading non-existent saga', async () => {
        const result = await repository.load('non-existent-id');
        expect(result).toBeNull();
      });

      it('should save and load saga snapshot round-trip', async () => {
        const snapshot: SagaSnapshot<TestSagaState> = {
          id: 'saga-123',
          state: {
            orderId: 'order-456',
            step: 1,
            amount: 100,
            completed: false
          },
          version: 1,
          createdAt: new Date(),
          updatedAt: new Date()
        };

        await repository.save(snapshot);
        const loaded = await repository.load('saga-123');

        expect(loaded).not.toBeNull();
        expect(loaded!.id).toBe(snapshot.id);
        expect(loaded!.state).toEqual(snapshot.state);
        expect(loaded!.version).toBe(snapshot.version);
        expect(loaded!.createdAt.getTime()).toBe(snapshot.createdAt.getTime());
        expect(loaded!.updatedAt.getTime()).toBe(snapshot.updatedAt.getTime());
      });

      it('should update existing saga snapshot', async () => {
        const initial: SagaSnapshot<TestSagaState> = {
          id: 'saga-123',
          state: {
            orderId: 'order-456',
            step: 1,
            amount: 100,
            completed: false
          },
          version: 1,
          createdAt: new Date(),
          updatedAt: new Date()
        };

        await repository.save(initial);

        const updated: SagaSnapshot<TestSagaState> = {
          ...initial,
          state: {
            ...initial.state,
            step: 2,
            completed: true
          },
          version: 2,
          updatedAt: new Date()
        };

        await repository.save(updated);
        const loaded = await repository.load('saga-123');

        expect(loaded!.state.step).toBe(2);
        expect(loaded!.state.completed).toBe(true);
        expect(loaded!.version).toBe(2);
      });

      it('should delete saga by id', async () => {
        const snapshot: SagaSnapshot<TestSagaState> = {
          id: 'saga-to-delete',
          state: { orderId: 'order-456', step: 1, amount: 100, completed: false },
          version: 1,
          createdAt: new Date(),
          updatedAt: new Date()
        };

        await repository.save(snapshot);
        expect(await repository.load('saga-to-delete')).not.toBeNull();

        await repository.delete('saga-to-delete');
        expect(await repository.load('saga-to-delete')).toBeNull();
      });
    });

    describe('Optimistic Concurrency Control', () => {
      it('should throw error when saving with stale version', async () => {
        const snapshot1: SagaSnapshot<TestSagaState> = {
          id: 'saga-123',
          state: {
            orderId: 'order-456',
            step: 1,
            amount: 100,
            completed: false
          },
          version: 1,
          createdAt: new Date(),
          updatedAt: new Date()
        };

        await repository.save(snapshot1);

        // Simulate two concurrent updates with same base version
        const update1 = { ...snapshot1, state: { ...snapshot1.state, step: 2 }, version: 2 };
        const update2 = { ...snapshot1, state: { ...snapshot1.state, amount: 200 }, version: 2 };

        await repository.save(update1);

        // Second save should fail due to version conflict
        await expect(repository.save(update2)).rejects.toThrow(SagaConcurrencyError);
      });
    });

    describe('Expiration and Cleanup', () => {
      it('should find expired sagas', async () => {
        const now = new Date();
        const past = new Date(now.getTime() - 60000); // 1 minute ago
        const future = new Date(now.getTime() + 60000); // 1 minute from now

        const expiredSaga: SagaSnapshot<TestSagaState> = {
          id: 'expired-saga',
          state: { orderId: 'order-1', step: 1, amount: 100, completed: false },
          version: 1,
          createdAt: past,
          updatedAt: past,
          expiresAt: past
        };

        const validSaga: SagaSnapshot<TestSagaState> = {
          id: 'valid-saga',
          state: { orderId: 'order-2', step: 1, amount: 200, completed: false },
          version: 1,
          createdAt: now,
          updatedAt: now,
          expiresAt: future
        };

        await repository.save(expiredSaga);
        await repository.save(validSaga);

        const expired = await repository.findExpired(now);
        expect(expired).toContain('expired-saga');
        expect(expired).not.toContain('valid-saga');
      });
    });

    describe('Query Operations', () => {
      it('should list sagas by status', async () => {
        const runningState = { orderId: 'order-1', step: 1, amount: 100, completed: false };
        const completedState = { orderId: 'order-2', step: 3, amount: 200, completed: true };

        await repository.save({
          id: 'running-saga',
          state: runningState,
          version: 1,
          createdAt: new Date(),
          updatedAt: new Date()
        });

        await repository.save({
          id: 'completed-saga',
          state: completedState,
          version: 1,
          createdAt: new Date(),
          updatedAt: new Date()
        });

        const running = await repository.findByStatus('running');
        const completed = await repository.findByStatus('completed');

        expect(running.map(s => s.id)).toContain('running-saga');
        expect(completed.map(s => s.id)).toContain('completed-saga');
      });

      it('should filter saga list correctly', async () => {
        const now = new Date();
        const past = new Date(now.getTime() - 60000);

        // Create test sagas
        await repository.save({
          id: 'saga-type-a',
          state: { orderId: 'order-1', step: 1, amount: 100, completed: false },
          version: 1,
          createdAt: past,
          updatedAt: past,
          sagaType: 'TypeA'
        });

        await repository.save({
          id: 'saga-type-b',
          state: { orderId: 'order-2', step: 1, amount: 200, completed: true },
          version: 1,
          createdAt: now,
          updatedAt: now,
          sagaType: 'TypeB'
        });

        // Test filtering by saga type
        const typeAResults = await repository.list({ sagaType: 'TypeA' });
        expect(typeAResults.map(s => s.id)).toContain('saga-type-a');
        expect(typeAResults.map(s => s.id)).not.toContain('saga-type-b');

        // Test filtering by creation date
        const recentResults = await repository.list({ createdAfter: past });
        expect(recentResults.map(s => s.id)).toContain('saga-type-b');

        // Test pagination
        const limitedResults = await repository.list({ limit: 1 });
        expect(limitedResults.length).toBe(1);
      });
    });

    describe('Special Characters and Edge Cases', () => {
      it('should handle saga IDs with special characters', async () => {
        const specialId = 'saga-with-special-chars-@#$%^&*()';
        const snapshot: SagaSnapshot<TestSagaState> = {
          id: specialId,
          state: { orderId: 'order-1', step: 1, amount: 100, completed: false },
          version: 1,
          createdAt: new Date(),
          updatedAt: new Date()
        };

        await repository.save(snapshot);
        const loaded = await repository.load(specialId);

        expect(loaded).not.toBeNull();
        expect(loaded!.id).toBe(specialId);
      });

      it('should handle large saga state objects', async () => {
        const largeState: TestSagaState = {
          orderId: 'order-' + 'x'.repeat(1000),
          step: 42,
          amount: 999999.99,
          completed: false
        };

        const snapshot: SagaSnapshot<TestSagaState> = {
          id: 'large-saga',
          state: largeState,
          version: 1,
          createdAt: new Date(),
          updatedAt: new Date(),
          metadata: {
            largeArray: Array(100).fill(0).map((_, i) => ({ index: i, data: 'test'.repeat(50) }))
          }
        };

        await repository.save(snapshot);
        const loaded = await repository.load('large-saga');

        expect(loaded).not.toBeNull();
        expect(loaded!.state.orderId).toBe(largeState.orderId);
        expect(loaded!.metadata?.largeArray).toHaveLength(100);
      });
    });
  };
}