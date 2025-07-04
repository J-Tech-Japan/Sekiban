import { describe } from 'vitest';
import { promises as fs } from 'fs';
import { join } from 'path';
import { tmpdir } from 'os';
import { createSagaRepositoryContractTests } from './saga-repository-contract.test';
import { InMemorySagaRepository } from './in-memory-saga-repository';
import { JsonFileSagaRepository } from './json-file-saga-repository';

describe('SagaRepository Contract Tests', () => {
  describe('InMemorySagaRepository', createSagaRepositoryContractTests(
    async () => new InMemorySagaRepository(),
    async (repo) => {
      if (repo instanceof InMemorySagaRepository) {
        repo.dispose();
      }
    }
  ));

  describe('JsonFileSagaRepository', createSagaRepositoryContractTests(
    async () => {
      const tempDir = join(tmpdir(), `saga-contract-test-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`);
      await fs.mkdir(tempDir, { recursive: true });
      
      return new JsonFileSagaRepository({
        dataDirectory: tempDir
      });
    },
    async (repo) => {
      if (repo instanceof JsonFileSagaRepository) {
        const config = (repo as any).config;
        repo.dispose();
        
        // Clean up temp directory
        try {
          await fs.rm(config.dataDirectory, { recursive: true, force: true });
        } catch (error) {
          // Ignore cleanup errors
        }
      }
    }
  ));
});