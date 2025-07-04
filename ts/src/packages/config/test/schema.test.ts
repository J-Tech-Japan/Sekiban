import { describe, it, expect } from 'vitest'
import {
  inMemoryConfigSchema,
  postgresConfigSchema,
  cosmosConfigSchema,
  storageConfigSchema,
  sekibanConfigSchema,
  envSchema
} from '../src/schema'

describe('Configuration Schemas', () => {
  describe('InMemory Config Schema', () => {
    it('should accept valid inmemory config', () => {
      const config = {
        type: 'inmemory',
        maxRetries: 5,
        retryDelay: 2000,
        timeout: 60000
      }
      
      const result = inMemoryConfigSchema.safeParse(config)
      expect(result.success).toBe(true)
      expect(result.data).toEqual(config)
    })
    
    it('should use defaults for optional fields', () => {
      const config = { type: 'inmemory' }
      
      const result = inMemoryConfigSchema.safeParse(config)
      expect(result.success).toBe(true)
      expect(result.data).toEqual({
        type: 'inmemory',
        maxRetries: 3,
        retryDelay: 1000,
        timeout: 30000
      })
    })
    
    it('should reject invalid type', () => {
      const config = { type: 'postgres' }
      
      const result = inMemoryConfigSchema.safeParse(config)
      expect(result.success).toBe(false)
    })
  })
  
  describe('PostgreSQL Config Schema', () => {
    it('should accept valid postgres config', () => {
      const config = {
        type: 'postgres',
        connectionString: 'postgresql://user:pass@localhost:5432/db',
        poolMin: 5,
        poolMax: 20,
        idleTimeoutMillis: 60000,
        connectionTimeoutMillis: 10000
      }
      
      const result = postgresConfigSchema.safeParse(config)
      expect(result.success).toBe(true)
      expect(result.data).toMatchObject(config)
    })
    
    it('should require connection string', () => {
      const config = { type: 'postgres' }
      
      const result = postgresConfigSchema.safeParse(config)
      expect(result.success).toBe(false)
    })
    
    it('should use defaults for pool settings', () => {
      const config = {
        type: 'postgres',
        connectionString: 'postgresql://localhost/test'
      }
      
      const result = postgresConfigSchema.safeParse(config)
      expect(result.success).toBe(true)
      expect(result.data.poolMin).toBe(2)
      expect(result.data.poolMax).toBe(10)
    })
  })
  
  describe('CosmosDB Config Schema', () => {
    it('should accept valid cosmos config', () => {
      const config = {
        type: 'cosmos',
        endpoint: 'https://myaccount.documents.azure.com:443/',
        key: 'mykey==',
        databaseId: 'mydb',
        containerId: 'mycontainer',
        consistencyLevel: 'Strong',
        maxItemCount: 200
      }
      
      const result = cosmosConfigSchema.safeParse(config)
      expect(result.success).toBe(true)
      expect(result.data).toMatchObject(config)
    })
    
    it('should require all cosmos fields', () => {
      const config = {
        type: 'cosmos',
        endpoint: 'https://myaccount.documents.azure.com:443/'
      }
      
      const result = cosmosConfigSchema.safeParse(config)
      expect(result.success).toBe(false)
    })
    
    it('should validate endpoint URL', () => {
      const config = {
        type: 'cosmos',
        endpoint: 'not-a-url',
        key: 'mykey==',
        databaseId: 'mydb',
        containerId: 'mycontainer'
      }
      
      const result = cosmosConfigSchema.safeParse(config)
      expect(result.success).toBe(false)
    })
  })
  
  describe('Storage Config Discriminated Union', () => {
    it('should accept inmemory config', () => {
      const config = { type: 'inmemory' }
      
      const result = storageConfigSchema.safeParse(config)
      expect(result.success).toBe(true)
    })
    
    it('should accept postgres config', () => {
      const config = {
        type: 'postgres',
        connectionString: 'postgresql://localhost/test'
      }
      
      const result = storageConfigSchema.safeParse(config)
      expect(result.success).toBe(true)
    })
    
    it('should accept cosmos config', () => {
      const config = {
        type: 'cosmos',
        endpoint: 'https://myaccount.documents.azure.com:443/',
        key: 'mykey==',
        databaseId: 'mydb',
        containerId: 'mycontainer'
      }
      
      const result = storageConfigSchema.safeParse(config)
      expect(result.success).toBe(true)
    })
    
    it('should reject unknown type', () => {
      const config = { type: 'unknown' }
      
      const result = storageConfigSchema.safeParse(config)
      expect(result.success).toBe(false)
    })
  })
  
  describe('Sekiban Config Schema', () => {
    it('should accept complete config', () => {
      const config = {
        storage: { type: 'inmemory' },
        defaultPartitionKey: 'tenant1',
        enableMetrics: true,
        enableTracing: true,
        logLevel: 'debug'
      }
      
      const result = sekibanConfigSchema.safeParse(config)
      expect(result.success).toBe(true)
      expect(result.data).toMatchObject(config)
    })
    
    it('should use defaults', () => {
      const config = {
        storage: { type: 'inmemory' }
      }
      
      const result = sekibanConfigSchema.safeParse(config)
      expect(result.success).toBe(true)
      expect(result.data).toEqual({
        storage: {
          type: 'inmemory',
          maxRetries: 3,
          retryDelay: 1000,
          timeout: 30000
        },
        defaultPartitionKey: 'default',
        enableMetrics: false,
        enableTracing: false,
        logLevel: 'info'
      })
    })
  })
  
  describe('Environment Schema', () => {
    it('should parse valid environment variables', () => {
      const env = {
        SEKIBAN_STORAGE_TYPE: 'postgres',
        SEKIBAN_POSTGRES_CONNECTION_STRING: 'postgresql://localhost/test',
        SEKIBAN_POSTGRES_POOL_MIN: '5',
        SEKIBAN_POSTGRES_POOL_MAX: '20',
        SEKIBAN_DEFAULT_PARTITION_KEY: 'tenant1',
        SEKIBAN_ENABLE_METRICS: 'true',
        SEKIBAN_ENABLE_TRACING: 'false',
        SEKIBAN_LOG_LEVEL: 'debug',
        SEKIBAN_MAX_RETRIES: '5',
        SEKIBAN_RETRY_DELAY: '2000',
        SEKIBAN_TIMEOUT: '60000'
      }
      
      const result = envSchema.safeParse(env)
      expect(result.success).toBe(true)
      expect(result.data.SEKIBAN_STORAGE_TYPE).toBe('postgres')
      expect(result.data.SEKIBAN_POSTGRES_POOL_MIN).toBe(5)
      expect(result.data.SEKIBAN_ENABLE_METRICS).toBe(true)
      expect(result.data.SEKIBAN_ENABLE_TRACING).toBe(false)
    })
    
    it('should transform string numbers', () => {
      const env = {
        SEKIBAN_POSTGRES_POOL_MIN: '10',
        SEKIBAN_MAX_RETRIES: '3'
      }
      
      const result = envSchema.safeParse(env)
      expect(result.success).toBe(true)
      expect(result.data.SEKIBAN_POSTGRES_POOL_MIN).toBe(10)
      expect(result.data.SEKIBAN_MAX_RETRIES).toBe(3)
    })
    
    it('should transform boolean strings', () => {
      const env = {
        SEKIBAN_ENABLE_METRICS: 'true',
        SEKIBAN_ENABLE_TRACING: 'false'
      }
      
      const result = envSchema.safeParse(env)
      expect(result.success).toBe(true)
      expect(result.data.SEKIBAN_ENABLE_METRICS).toBe(true)
      expect(result.data.SEKIBAN_ENABLE_TRACING).toBe(false)
    })
    
    it('should use default storage type', () => {
      const env = {}
      
      const result = envSchema.safeParse(env)
      expect(result.success).toBe(true)
      expect(result.data.SEKIBAN_STORAGE_TYPE).toBe('inmemory')
    })
  })
})