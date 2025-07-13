import { describe, it, expect, vi } from 'vitest'
import {
  loadConfig,
  loadConfigFromJson,
  validateConfig,
  ConfigValidationError
} from '../src/loader'

describe('Configuration Loader', () => {
  describe('loadConfig', () => {
    it('should load inmemory config from environment', () => {
      const env = {
        SEKIBAN_STORAGE_TYPE: 'inmemory',
        SEKIBAN_DEFAULT_PARTITION_KEY: 'test',
        SEKIBAN_ENABLE_METRICS: 'true',
        SEKIBAN_LOG_LEVEL: 'debug'
      }
      
      const config = loadConfig({ env })
      
      expect(config.storage.type).toBe('inmemory')
      expect(config.defaultPartitionKey).toBe('test')
      expect(config.enableMetrics).toBe(true)
      expect(config.logLevel).toBe('debug')
    })
    
    it('should load postgres config from environment', () => {
      const env = {
        SEKIBAN_STORAGE_TYPE: 'postgres',
        SEKIBAN_POSTGRES_CONNECTION_STRING: 'postgresql://localhost:5432/test',
        SEKIBAN_POSTGRES_POOL_MIN: '5',
        SEKIBAN_POSTGRES_POOL_MAX: '20'
      }
      
      const config = loadConfig({ env })
      
      expect(config.storage.type).toBe('postgres')
      expect(config.storage).toMatchObject({
        type: 'postgres',
        connectionString: 'postgresql://localhost:5432/test',
        poolMin: 5,
        poolMax: 20
      })
    })
    
    it('should load cosmos config from environment', () => {
      const env = {
        SEKIBAN_STORAGE_TYPE: 'cosmos',
        SEKIBAN_COSMOS_ENDPOINT: 'https://myaccount.documents.azure.com:443/',
        SEKIBAN_COSMOS_KEY: 'mykey==',
        SEKIBAN_COSMOS_DATABASE_ID: 'mydb',
        SEKIBAN_COSMOS_CONTAINER_ID: 'mycontainer',
        SEKIBAN_COSMOS_CONSISTENCY_LEVEL: 'Strong'
      }
      
      const config = loadConfig({ env })
      
      expect(config.storage.type).toBe('cosmos')
      expect(config.storage).toMatchObject({
        type: 'cosmos',
        endpoint: 'https://myaccount.documents.azure.com:443/',
        key: 'mykey==',
        databaseId: 'mydb',
        containerId: 'mycontainer',
        consistencyLevel: 'Strong'
      })
    })
    
    it('should throw error for missing postgres connection string', () => {
      const env = {
        SEKIBAN_STORAGE_TYPE: 'postgres'
      }
      
      expect(() => loadConfig({ env })).toThrow('PostgreSQL connection string is required')
    })
    
    it('should throw error for incomplete cosmos config', () => {
      const env = {
        SEKIBAN_STORAGE_TYPE: 'cosmos',
        SEKIBAN_COSMOS_ENDPOINT: 'https://myaccount.documents.azure.com:443/'
      }
      
      expect(() => loadConfig({ env })).toThrow('CosmosDB configuration is incomplete')
    })
    
    it('should return default config on validation error when throwOnError is false', () => {
      const env = {
        SEKIBAN_STORAGE_TYPE: 'invalid'
      }
      
      const config = loadConfig({ env, throwOnError: false })
      
      expect(config).toEqual({
        storage: { type: 'inmemory' },
        defaultPartitionKey: 'default',
        enableMetrics: false,
        enableTracing: false,
        logLevel: 'info'
      })
    })
    
    it('should use defaults when no environment variables set', () => {
      const config = loadConfig({ env: {} })
      
      expect(config).toEqual({
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
    
    it('should handle retry configuration', () => {
      const env = {
        SEKIBAN_STORAGE_TYPE: 'inmemory',
        SEKIBAN_MAX_RETRIES: '5',
        SEKIBAN_RETRY_DELAY: '2000',
        SEKIBAN_TIMEOUT: '60000'
      }
      
      const config = loadConfig({ env })
      
      expect(config.storage).toMatchObject({
        maxRetries: 5,
        retryDelay: 2000,
        timeout: 60000
      })
    })
  })
  
  describe('loadConfigFromJson', () => {
    it('should load valid JSON configuration', () => {
      const json = JSON.stringify({
        storage: {
          type: 'postgres',
          connectionString: 'postgresql://localhost/test',
          poolMin: 5,
          poolMax: 20
        },
        defaultPartitionKey: 'tenant1',
        enableMetrics: true,
        enableTracing: false,
        logLevel: 'debug'
      })
      
      const config = loadConfigFromJson(json)
      
      expect(config.storage.type).toBe('postgres')
      expect(config.defaultPartitionKey).toBe('tenant1')
      expect(config.enableMetrics).toBe(true)
      expect(config.logLevel).toBe('debug')
    })
    
    it('should throw ConfigValidationError for invalid JSON', () => {
      const json = JSON.stringify({
        storage: { type: 'invalid' }
      })
      
      expect(() => loadConfigFromJson(json)).toThrow(ConfigValidationError)
    })
    
    it('should throw error for invalid JSON syntax', () => {
      const json = 'not valid json'
      
      expect(() => loadConfigFromJson(json)).toThrow()
    })
  })
  
  describe('validateConfig', () => {
    it('should return true for valid config', () => {
      const config = {
        storage: { type: 'inmemory' },
        defaultPartitionKey: 'default',
        enableMetrics: false,
        enableTracing: false,
        logLevel: 'info'
      }
      
      expect(validateConfig(config)).toBe(true)
    })
    
    it('should return false for invalid config', () => {
      const config = {
        storage: { type: 'invalid' }
      }
      
      expect(validateConfig(config)).toBe(false)
    })
    
    it('should return false for missing required fields', () => {
      const config = {
        defaultPartitionKey: 'default'
      }
      
      expect(validateConfig(config)).toBe(false)
    })
  })
  
  describe('ConfigValidationError', () => {
    it('should include error details', () => {
      const env = {
        SEKIBAN_STORAGE_TYPE: 'invalid'
      }
      
      try {
        loadConfig({ env })
      } catch (error) {
        expect(error).toBeInstanceOf(ConfigValidationError)
        expect((error as ConfigValidationError).errors).toBeDefined()
        expect((error as ConfigValidationError).name).toBe('ConfigValidationError')
      }
    })
  })
})