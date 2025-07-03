import { describe } from 'vitest'
import { InMemoryStorageProvider } from './in-memory-storage-provider'
import { defineStorageContractTests } from '@sekiban/testing'
import { 
  StorageProviderConfig,
  StorageProviderType,
  IEventStorageProvider
} from './storage-provider'

describe('InMemoryStorageProvider Contract Tests', () => {
  const createProvider = async (): Promise<IEventStorageProvider> => {
    const config: StorageProviderConfig = {
      type: StorageProviderType.InMemory
    }

    const provider = new InMemoryStorageProvider(config)
    const initResult = await provider.initialize()
    
    if (initResult.isErr()) {
      throw initResult.error
    }

    return provider
  }

  const cleanup = async (provider: IEventStorageProvider): Promise<void> => {
    await provider.close()
  }

  // Define the contract test suite
  defineStorageContractTests(
    'InMemoryStorageProvider',
    createProvider,
    cleanup
  )
})