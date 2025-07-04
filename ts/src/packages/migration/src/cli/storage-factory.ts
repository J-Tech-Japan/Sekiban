import { 
  IEventStorageProvider, 
  InMemoryStorageProvider,
  StorageProviderType,
  StorageProviderConfig
} from '@sekiban/core'

/**
 * Create a storage provider based on configuration
 */
export async function createStorageProvider(config: {
  type: string
  connectionString?: string
  databaseName?: string
}): Promise<IEventStorageProvider> {
  let providerConfig: StorageProviderConfig
  
  switch (config.type) {
    case 'inmemory':
      providerConfig = {
        type: StorageProviderType.InMemory
      }
      const inMemoryProvider = new InMemoryStorageProvider(providerConfig)
      await inMemoryProvider.initialize()
      return inMemoryProvider
    
    case 'postgres':
      // Would import from @sekiban/postgres
      throw new Error('PostgreSQL provider not available in CLI yet')
    
    case 'cosmos':
      // Would import from @sekiban/cosmos
      throw new Error('CosmosDB provider not available in CLI yet')
    
    default:
      throw new Error(`Unsupported storage type: ${config.type}`)
  }
}