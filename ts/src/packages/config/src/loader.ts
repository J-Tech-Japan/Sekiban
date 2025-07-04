import { config as dotenvConfig } from 'dotenv'
import { z } from 'zod'
import { 
  SekibanConfig, 
  StorageConfig,
  envSchema,
  sekibanConfigSchema
} from './schema'

/**
 * Configuration loading options
 */
export interface ConfigLoaderOptions {
  /**
   * Path to .env file
   */
  envPath?: string
  
  /**
   * Override environment variables (useful for testing)
   */
  env?: Record<string, string | undefined>
  
  /**
   * Throw error if configuration is invalid
   */
  throwOnError?: boolean
}

/**
 * Configuration validation error
 */
export class ConfigValidationError extends Error {
  constructor(
    message: string,
    public readonly errors: z.ZodError
  ) {
    super(message)
    this.name = 'ConfigValidationError'
  }
}

/**
 * Load configuration from environment variables
 */
export function loadConfig(options: ConfigLoaderOptions = {}): SekibanConfig {
  // Load .env file if path provided
  if (options.envPath) {
    dotenvConfig({ path: options.envPath })
  }
  
  // Use provided env or process.env
  const env = options.env || process.env
  
  // Parse environment variables
  const envResult = envSchema.safeParse(env)
  
  if (!envResult.success) {
    const error = new ConfigValidationError(
      'Invalid environment configuration',
      envResult.error
    )
    
    if (options.throwOnError !== false) {
      throw error
    }
    
    // Return default configuration
    return {
      storage: { type: 'inmemory' },
      defaultPartitionKey: 'default',
      enableMetrics: false,
      enableTracing: false,
      logLevel: 'info'
    }
  }
  
  const envConfig = envResult.data
  
  // Build storage configuration based on type
  let storageConfig: StorageConfig
  
  switch (envConfig.SEKIBAN_STORAGE_TYPE) {
    case 'inmemory':
      storageConfig = {
        type: 'inmemory',
        maxRetries: envConfig.SEKIBAN_MAX_RETRIES || 3,
        retryDelay: envConfig.SEKIBAN_RETRY_DELAY || 1000,
        timeout: envConfig.SEKIBAN_TIMEOUT || 30000
      }
      break
    
    case 'postgres':
      if (!envConfig.SEKIBAN_POSTGRES_CONNECTION_STRING) {
        throw new Error('PostgreSQL connection string is required')
      }
      
      storageConfig = {
        type: 'postgres',
        connectionString: envConfig.SEKIBAN_POSTGRES_CONNECTION_STRING,
        poolMin: envConfig.SEKIBAN_POSTGRES_POOL_MIN || 2,
        poolMax: envConfig.SEKIBAN_POSTGRES_POOL_MAX || 10,
        idleTimeoutMillis: 30000,
        connectionTimeoutMillis: 5000,
        maxRetries: envConfig.SEKIBAN_MAX_RETRIES || 3,
        retryDelay: envConfig.SEKIBAN_RETRY_DELAY || 1000,
        timeout: envConfig.SEKIBAN_TIMEOUT || 30000
      }
      break
    
    case 'cosmos':
      if (!envConfig.SEKIBAN_COSMOS_ENDPOINT || 
          !envConfig.SEKIBAN_COSMOS_KEY ||
          !envConfig.SEKIBAN_COSMOS_DATABASE_ID ||
          !envConfig.SEKIBAN_COSMOS_CONTAINER_ID) {
        throw new Error('CosmosDB configuration is incomplete')
      }
      
      storageConfig = {
        type: 'cosmos',
        endpoint: envConfig.SEKIBAN_COSMOS_ENDPOINT,
        key: envConfig.SEKIBAN_COSMOS_KEY,
        databaseId: envConfig.SEKIBAN_COSMOS_DATABASE_ID,
        containerId: envConfig.SEKIBAN_COSMOS_CONTAINER_ID,
        consistencyLevel: envConfig.SEKIBAN_COSMOS_CONSISTENCY_LEVEL || 'Session',
        maxItemCount: 100,
        maxRetries: envConfig.SEKIBAN_MAX_RETRIES || 3,
        retryDelay: envConfig.SEKIBAN_RETRY_DELAY || 1000,
        timeout: envConfig.SEKIBAN_TIMEOUT || 30000
      }
      break
  }
  
  // Build full configuration
  const config: SekibanConfig = {
    storage: storageConfig,
    defaultPartitionKey: envConfig.SEKIBAN_DEFAULT_PARTITION_KEY || 'default',
    enableMetrics: envConfig.SEKIBAN_ENABLE_METRICS || false,
    enableTracing: envConfig.SEKIBAN_ENABLE_TRACING || false,
    logLevel: envConfig.SEKIBAN_LOG_LEVEL || 'info'
  }
  
  // Validate final configuration
  const configResult = sekibanConfigSchema.safeParse(config)
  
  if (!configResult.success) {
    const error = new ConfigValidationError(
      'Invalid Sekiban configuration',
      configResult.error
    )
    
    if (options.throwOnError !== false) {
      throw error
    }
  }
  
  return config
}

/**
 * Load configuration from JSON string
 */
export function loadConfigFromJson(json: string): SekibanConfig {
  const parsed = JSON.parse(json)
  const result = sekibanConfigSchema.safeParse(parsed)
  
  if (!result.success) {
    throw new ConfigValidationError(
      'Invalid configuration JSON',
      result.error
    )
  }
  
  return result.data
}

/**
 * Validate a configuration object
 */
export function validateConfig(config: unknown): config is SekibanConfig {
  return sekibanConfigSchema.safeParse(config).success
}