import { IEventStorageProvider, InMemoryStorageProvider } from '@sekiban/core'
import { 
  StorageConfig, 
  InMemoryConfig, 
  PostgresConfig, 
  CosmosConfig 
} from '../schema'

/**
 * Create a storage provider instance based on configuration
 */
export async function createStorageProvider(config: StorageConfig): Promise<IEventStorageProvider> {
  switch (config.type) {
    case 'inmemory':
      return createInMemoryProvider(config)
    
    case 'postgres':
      return createPostgresProvider(config)
    
    case 'cosmos':
      return createCosmosProvider(config)
    
    default:
      // TypeScript exhaustiveness check
      const _exhaustive: never = config
      throw new Error(`Unsupported storage type: ${(config as any).type}`)
  }
}

/**
 * Create InMemory storage provider
 */
async function createInMemoryProvider(config: InMemoryConfig): Promise<IEventStorageProvider> {
  return new InMemoryStorageProvider({
    maxRetries: config.maxRetries,
    retryDelay: config.retryDelay,
    timeout: config.timeout
  })
}

/**
 * Create PostgreSQL storage provider
 */
async function createPostgresProvider(config: PostgresConfig): Promise<IEventStorageProvider> {
  // Dynamic import to avoid dependency issues when not using PostgreSQL
  const { PostgresEventStore } = await import('@sekiban/postgres')
  const { Pool } = await import('pg')
  
  const pool = new Pool({
    connectionString: config.connectionString,
    min: config.poolMin,
    max: config.poolMax,
    idleTimeoutMillis: config.idleTimeoutMillis,
    connectionTimeoutMillis: config.connectionTimeoutMillis
  })
  
  return new PostgresEventStore(pool)
}

/**
 * Create CosmosDB storage provider
 */
async function createCosmosProvider(config: CosmosConfig): Promise<IEventStorageProvider> {
  // Dynamic import to avoid dependency issues when not using CosmosDB
  const { CosmosEventStore } = await import('@sekiban/cosmos')
  const { CosmosClient } = await import('@azure/cosmos')
  
  const client = new CosmosClient({
    endpoint: config.endpoint,
    key: config.key,
    consistencyLevel: config.consistencyLevel
  })
  
  return new CosmosEventStore(
    client,
    config.databaseId,
    config.containerId,
    {
      maxItemCount: config.maxItemCount,
      maxRetries: config.maxRetries,
      retryDelay: config.retryDelay,
      timeout: config.timeout
    }
  )
}

/**
 * Storage provider factory configuration
 */
export interface StorageProviderFactory {
  create(config: StorageConfig): Promise<IEventStorageProvider>
}

/**
 * Default storage provider factory
 */
export const defaultStorageProviderFactory: StorageProviderFactory = {
  create: createStorageProvider
}