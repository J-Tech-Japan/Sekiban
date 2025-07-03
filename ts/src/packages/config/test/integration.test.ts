import { describe, it, expect } from 'vitest'
import { loadConfig, validateConfig } from '../src'
import type { SekibanConfig } from '../src'

describe('Config Integration Tests', () => {
  it('should load complete configuration from environment', () => {
    const env = {
      SEKIBAN_STORAGE_TYPE: 'postgres',
      SEKIBAN_POSTGRES_CONNECTION_STRING: 'postgresql://user:pass@localhost:5432/sekiban',
      SEKIBAN_POSTGRES_POOL_MIN: '10',
      SEKIBAN_POSTGRES_POOL_MAX: '50',
      SEKIBAN_DEFAULT_PARTITION_KEY: 'production',
      SEKIBAN_ENABLE_METRICS: 'true',
      SEKIBAN_ENABLE_TRACING: 'true',
      SEKIBAN_LOG_LEVEL: 'warn',
      SEKIBAN_MAX_RETRIES: '5',
      SEKIBAN_RETRY_DELAY: '2000',
      SEKIBAN_TIMEOUT: '60000'
    }
    
    const config = loadConfig({ env })
    
    expect(config).toEqual({
      storage: {
        type: 'postgres',
        connectionString: 'postgresql://user:pass@localhost:5432/sekiban',
        poolMin: 10,
        poolMax: 50,
        idleTimeoutMillis: 30000,
        connectionTimeoutMillis: 5000,
        maxRetries: 5,
        retryDelay: 2000,
        timeout: 60000
      },
      defaultPartitionKey: 'production',
      enableMetrics: true,
      enableTracing: true,
      logLevel: 'warn'
    })
  })
  
  it('should validate configuration objects', () => {
    const validConfig: SekibanConfig = {
      storage: {
        type: 'cosmos',
        endpoint: 'https://myaccount.documents.azure.com:443/',
        key: 'mykey==',
        databaseId: 'sekiban',
        containerId: 'events',
        consistencyLevel: 'Session',
        maxItemCount: 100,
        maxRetries: 3,
        retryDelay: 1000,
        timeout: 30000
      },
      defaultPartitionKey: 'default',
      enableMetrics: false,
      enableTracing: false,
      logLevel: 'info'
    }
    
    expect(validateConfig(validConfig)).toBe(true)
    expect(validateConfig({ invalid: 'config' })).toBe(false)
  })
  
  it('should handle mixed environment configurations', () => {
    // Test partial configurations with defaults
    const env = {
      SEKIBAN_STORAGE_TYPE: 'inmemory',
      SEKIBAN_LOG_LEVEL: 'debug'
    }
    
    const config = loadConfig({ env })
    
    expect(config.storage.type).toBe('inmemory')
    expect(config.logLevel).toBe('debug')
    expect(config.defaultPartitionKey).toBe('default')
    expect(config.enableMetrics).toBe(false)
  })
})