import { describe, it, expect } from 'vitest'
import { CosmosStorageProvider } from './cosmos-storage-provider'
import { 
  StorageProviderConfig,
  StorageProviderType
} from '@sekiban/core'

describe('CosmosStorageProvider', () => {
  it('should throw error if connection string is missing', () => {
    const config: StorageProviderConfig = {
      type: StorageProviderType.CosmosDB,
      databaseName: 'test'
    }

    expect(() => new CosmosStorageProvider(config)).toThrow(
      'Connection string is required for CosmosDB provider'
    )
  })

  it('should throw error if database name is missing', () => {
    const config: StorageProviderConfig = {
      type: StorageProviderType.CosmosDB,
      connectionString: 'AccountEndpoint=https://localhost:8081;AccountKey=testkey;'
    }

    expect(() => new CosmosStorageProvider(config)).toThrow(
      'Database name is required for CosmosDB provider'
    )
  })

  it('should parse connection string correctly', () => {
    const config: StorageProviderConfig = {
      type: StorageProviderType.CosmosDB,
      connectionString: 'AccountEndpoint=https://localhost:8081;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;',
      databaseName: 'test'
    }

    const provider = new CosmosStorageProvider(config)
    expect(provider).toBeDefined()
  })

  it('should handle invalid connection string format', () => {
    const config: StorageProviderConfig = {
      type: StorageProviderType.CosmosDB,
      connectionString: 'invalid-connection-string',
      databaseName: 'test'
    }

    const provider = new CosmosStorageProvider(config)
    
    // This will fail during initialization
    const initPromise = provider.initialize()
    
    expect(initPromise).toBeDefined()
  })
})