import { describe, it, expect, beforeAll, afterAll } from 'vitest'
import { GenericContainer, StartedTestContainer } from 'testcontainers'
import { CosmosClient } from '@azure/cosmos'
import { CosmosStorageProvider } from './cosmos-storage-provider'
import { defineStorageContractTests } from '@sekiban/testing'
import { 
  StorageProviderConfig,
  StorageProviderType,
  IEventStorageProvider
} from '@sekiban/core'

describe('CosmosStorageProvider Contract Tests', () => {
  let container: StartedTestContainer
  let connectionString: string
  let cosmosClient: CosmosClient

  // Setup CosmosDB emulator once for all tests
  beforeAll(async () => {
    container = await new GenericContainer('mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest')
      .withEnvironment({
        AZURE_COSMOS_EMULATOR_PARTITION_COUNT: '1',
        AZURE_COSMOS_EMULATOR_ENABLE_DATA_PERSISTENCE: 'false',
        AZURE_COSMOS_EMULATOR_IP_ADDRESS_OVERRIDE: '127.0.0.1'
      })
      .withExposedPorts(8081, 10251, 10252, 10253, 10254)
      .withStartupTimeout(120000)
      .start()

    const host = container.getHost()
    const port = container.getMappedPort(8081)
    const endpoint = `https://${host}:${port}`
    const key = 'C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==' // Default emulator key
    
    connectionString = `AccountEndpoint=${endpoint};AccountKey=${key};`
    
    // Create client for cleanup
    cosmosClient = new CosmosClient({
      endpoint,
      key,
      connectionPolicy: {
        requestTimeout: 10000,
        enableEndpointDiscovery: false
      }
    })
  }, 120000)

  afterAll(async () => {
    await container.stop()
  })

  const createProvider = async (): Promise<IEventStorageProvider> => {
    const config: StorageProviderConfig = {
      type: StorageProviderType.CosmosDB,
      connectionString,
      databaseName: 'sekiban_contract_test',
      maxRetries: 3,
      timeoutMs: 30000
    }

    const provider = new CosmosStorageProvider(config)
    const initResult = await provider.initialize()
    
    if (initResult.isErr()) {
      throw initResult.error
    }

    // Clear containers for clean test
    const database = cosmosClient.database('sekiban_contract_test')
    try {
      await database.container('events').delete()
      await database.container('snapshots').delete()
    } catch {
      // Containers might not exist
    }
    
    // Reinitialize after cleanup
    await provider.initialize()

    return provider
  }

  const cleanup = async (provider: IEventStorageProvider): Promise<void> => {
    await provider.close()
  }

  // Define the contract test suite
  defineStorageContractTests(
    'CosmosStorageProvider',
    createProvider,
    cleanup
  )
})