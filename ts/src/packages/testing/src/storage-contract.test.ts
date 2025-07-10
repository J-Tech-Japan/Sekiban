import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { StorageContractTestSuite } from './storage-contract'
import { 
  InMemoryStorageProvider,
  StorageProviderConfig,
  StorageProviderType,
  IEventStorageProvider
} from '@sekiban/core'

describe('StorageContractTestSuite', () => {
  it('should export a test suite function', () => {
    expect(StorageContractTestSuite).toBeDefined()
    expect(typeof StorageContractTestSuite).toBe('function')
  })

  it('should run contract tests for InMemoryStorageProvider', async () => {
    const config: StorageProviderConfig = {
      type: StorageProviderType.InMemory
    }
    
    const createProvider = async (): Promise<IEventStorageProvider> => {
      const provider = new InMemoryStorageProvider(config)
      await provider.initialize()
      return provider
    }

    const cleanup = async (provider: IEventStorageProvider): Promise<void> => {
      await provider.close()
    }

    // This should not throw
    await StorageContractTestSuite(
      'InMemoryStorageProvider',
      createProvider,
      cleanup
    )
  })
})