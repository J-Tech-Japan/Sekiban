import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { promises as fs } from 'fs';
import { join } from 'path';
import { tmpdir } from 'os';
import { SagaRepository, SagaSnapshot, SagaConcurrencyError } from './saga-repository';
import { JsonFileSagaRepository } from './json-file-saga-repository';

// Test saga state
interface TestSagaState {
  orderId: string;
  step: number;
  amount: number;
  completed: boolean;
}

describe('JsonFileSagaRepository', () => {
  let repository: SagaRepository<TestSagaState>;
  let tempDir: string;

  beforeEach(async () => {
    // Create a temporary directory for each test
    tempDir = join(tmpdir(), `saga-test-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`);
    await fs.mkdir(tempDir, { recursive: true });
    
    repository = new JsonFileSagaRepository<TestSagaState>({
      dataDirectory: tempDir
    });
  });

  afterEach(async () => {
    // Clean up temporary directory
    try {
      await fs.rm(tempDir, { recursive: true, force: true });
    } catch (error) {
      // Ignore cleanup errors
    }
    
    if (repository instanceof JsonFileSagaRepository) {
      repository.dispose();
    }
  });

  describe('Basic File Operations', () => {
    it('should create saga files in the data directory', async () => {
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

      // Check that the file was created
      const filePath = join(tempDir, 'saga-123.json');
      const fileExists = await fs.access(filePath).then(() => true).catch(() => false);
      expect(fileExists).toBe(true);

      // Check file contents
      const fileContent = await fs.readFile(filePath, 'utf-8');
      const savedData = JSON.parse(fileContent);
      expect(savedData.id).toBe('saga-123');
      expect(savedData.state.orderId).toBe('order-456');
    });

    it('should handle concurrent writes with file locking', async () => {
      const snapshot1: SagaSnapshot<TestSagaState> = {
        id: 'saga-concurrent',
        state: { orderId: 'order-1', step: 1, amount: 100, completed: false },
        version: 1,
        createdAt: new Date(),
        updatedAt: new Date()
      };

      // Save initial snapshot
      await repository.save(snapshot1);

      // Simulate concurrent updates
      const update1 = { ...snapshot1, state: { ...snapshot1.state, step: 2 }, version: 2 };
      const update2 = { ...snapshot1, state: { ...snapshot1.state, amount: 200 }, version: 2 };

      // First update should succeed
      await repository.save(update1);

      // Second update should fail due to version conflict
      await expect(repository.save(update2)).rejects.toThrow(SagaConcurrencyError);
    });

    it('should clean up saga files when deleted', async () => {
      const snapshot: SagaSnapshot<TestSagaState> = {
        id: 'saga-to-delete',
        state: { orderId: 'order-456', step: 1, amount: 100, completed: false },
        version: 1,
        createdAt: new Date(),
        updatedAt: new Date()
      };

      await repository.save(snapshot);
      
      const filePath = join(tempDir, 'saga-to-delete.json');
      let fileExists = await fs.access(filePath).then(() => true).catch(() => false);
      expect(fileExists).toBe(true);

      await repository.delete('saga-to-delete');
      
      fileExists = await fs.access(filePath).then(() => true).catch(() => false);
      expect(fileExists).toBe(false);
    });
  });

  describe('Contract Compliance', () => {
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
      expect(new Date(loaded!.createdAt)).toEqual(snapshot.createdAt);
    });

    it('should find expired sagas', async () => {
      const now = new Date();
      const past = new Date(now.getTime() - 60000); // 1 minute ago

      const expiredSaga: SagaSnapshot<TestSagaState> = {
        id: 'expired-saga',
        state: { orderId: 'order-1', step: 1, amount: 100, completed: false },
        version: 1,
        createdAt: past,
        updatedAt: past,
        expiresAt: past
      };

      await repository.save(expiredSaga);

      const expired = await repository.findExpired(now);
      expect(expired).toContain('expired-saga');
    });

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
  });

  describe('Error Handling', () => {
    it('should handle corrupted JSON files gracefully', async () => {
      // Create a corrupted file
      const corruptedPath = join(tempDir, 'corrupted-saga.json');
      await fs.writeFile(corruptedPath, 'invalid json {');

      const result = await repository.load('corrupted-saga');
      expect(result).toBeNull();
    });

    it('should handle missing data directory', async () => {
      const nonExistentDir = join(tempDir, 'non-existent');
      const repoWithMissingDir = new JsonFileSagaRepository<TestSagaState>({
        dataDirectory: nonExistentDir
      });

      // Should create directory and work normally
      const snapshot: SagaSnapshot<TestSagaState> = {
        id: 'test-saga',
        state: { orderId: 'order-1', step: 1, amount: 100, completed: false },
        version: 1,
        createdAt: new Date(),
        updatedAt: new Date()
      };

      await repoWithMissingDir.save(snapshot);
      const loaded = await repoWithMissingDir.load('test-saga');
      
      expect(loaded).not.toBeNull();
      expect(loaded!.id).toBe('test-saga');
      
      repoWithMissingDir.dispose();
    });
  });
});