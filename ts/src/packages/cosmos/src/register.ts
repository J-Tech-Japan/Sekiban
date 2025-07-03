import { ResultAsync } from 'neverthrow'
import { 
  StorageProviderFactory, 
  StorageProviderType,
  StorageProviderConfig,
  IEventStorageProvider
} from '@sekiban/core'
import { CosmosStorageProvider } from './cosmos-storage-provider'

/**
 * Register CosmosDB storage provider with the factory
 */
export function registerCosmosProvider(): void {
  StorageProviderFactory.register(
    StorageProviderType.CosmosDB,
    (config: StorageProviderConfig): ResultAsync<IEventStorageProvider, any> => {
      const provider = new CosmosStorageProvider(config)
      return ResultAsync.fromSafePromise(
        provider.initialize().then(() => provider)
      )
    }
  )
}