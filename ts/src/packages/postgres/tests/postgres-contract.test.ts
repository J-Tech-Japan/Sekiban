import { describe, it, expect } from 'vitest'
import { GenericContainer, StartedTestContainer } from 'testcontainers'
import { Pool } from 'pg'
import { PostgresStorageProvider } from './postgres-storage-provider'
import { defineStorageContractTests } from '@sekiban/testing'
import { 
  StorageProviderConfig,
  StorageProviderType,
  IEventStorageProvider
} from '@sekiban/core'

describe('PostgresStorageProvider Contract Tests', () => {
  let container: StartedTestContainer
  let connectionString: string

  // Setup PostgreSQL container once for all tests
  beforeAll(async () => {
    container = await new GenericContainer('postgres:16-alpine')
      .withEnvironment({
        POSTGRES_DB: 'sekiban_contract_test',
        POSTGRES_USER: 'test',
        POSTGRES_PASSWORD: 'test'
      })
      .withExposedPorts(5432)
      .start()

    const host = container.getHost()
    const port = container.getMappedPort(5432)
    connectionString = `postgresql://test:test@${host}:${port}/sekiban_contract_test`
  }, 60000)

  afterAll(async () => {
    await container.stop()
  })

  const createProvider = async (): Promise<IEventStorageProvider> => {
    const config: StorageProviderConfig = {
      type: StorageProviderType.PostgreSQL,
      connectionString,
      maxRetries: 10,
      timeoutMs: 30000
    }

    const provider = new PostgresStorageProvider(config)
    const initResult = await provider.initialize()
    
    if (initResult.isErr()) {
      throw initResult.error
    }

    // Clear tables for clean test
    const pool = new Pool({ connectionString })
    await pool.query('TRUNCATE TABLE events, snapshots')
    await pool.end()

    return provider
  }

  const cleanup = async (provider: IEventStorageProvider): Promise<void> => {
    await provider.close()
  }

  // Define the contract test suite
  defineStorageContractTests(
    'PostgresStorageProvider',
    createProvider,
    cleanup
  )
})