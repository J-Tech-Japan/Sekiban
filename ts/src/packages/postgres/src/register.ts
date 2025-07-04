import { ResultAsync } from 'neverthrow'
import { 
  StorageProviderFactory, 
  StorageProviderType,
  StorageProviderConfig,
  IEventStorageProvider
} from '@sekiban/core'
import { PostgresStorageProvider } from './postgres-storage-provider'

/**
 * Register PostgreSQL storage provider with the factory
 */
export function registerPostgresProvider(): void {
  StorageProviderFactory.register(
    StorageProviderType.PostgreSQL,
    (config: StorageProviderConfig): ResultAsync<IEventStorageProvider, any> => {
      const provider = new PostgresStorageProvider(config)
      return ResultAsync.fromSafePromise(
        provider.initialize().then(() => provider)
      )
    }
  )
}