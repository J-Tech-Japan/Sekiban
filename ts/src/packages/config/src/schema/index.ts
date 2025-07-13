import { z } from 'zod'

/**
 * Base configuration shared by all storage providers
 */
const baseStorageSchema = z.object({
  maxRetries: z.number().int().positive().default(3),
  retryDelay: z.number().int().positive().default(1000),
  timeout: z.number().int().positive().default(30000)
})

/**
 * InMemory storage configuration
 */
export const inMemoryConfigSchema = z.object({
  type: z.literal('inmemory'),
  ...baseStorageSchema.shape
})

/**
 * PostgreSQL storage configuration
 */
export const postgresConfigSchema = z.object({
  type: z.literal('postgres'),
  connectionString: z.string().min(1),
  poolMin: z.number().int().min(0).default(2),
  poolMax: z.number().int().positive().default(10),
  idleTimeoutMillis: z.number().int().positive().default(30000),
  connectionTimeoutMillis: z.number().int().positive().default(5000),
  ...baseStorageSchema.shape
})

/**
 * CosmosDB storage configuration
 */
export const cosmosConfigSchema = z.object({
  type: z.literal('cosmos'),
  endpoint: z.string().url(),
  key: z.string().min(1),
  databaseId: z.string().min(1),
  containerId: z.string().min(1),
  consistencyLevel: z.enum(['Strong', 'BoundedStaleness', 'Session', 'ConsistentPrefix', 'Eventual']).default('Session'),
  maxItemCount: z.number().int().positive().default(100),
  ...baseStorageSchema.shape
})

/**
 * Discriminated union for storage configuration
 */
export const storageConfigSchema = z.discriminatedUnion('type', [
  inMemoryConfigSchema,
  postgresConfigSchema,
  cosmosConfigSchema
])

/**
 * Main Sekiban configuration schema
 */
export const sekibanConfigSchema = z.object({
  storage: storageConfigSchema,
  defaultPartitionKey: z.string().default('default'),
  enableMetrics: z.boolean().default(false),
  enableTracing: z.boolean().default(false),
  logLevel: z.enum(['debug', 'info', 'warn', 'error']).default('info')
})

/**
 * Environment variable schema
 */
export const envSchema = z.object({
  // Storage type
  SEKIBAN_STORAGE_TYPE: z.enum(['inmemory', 'postgres', 'cosmos']).default('inmemory'),
  
  // PostgreSQL
  SEKIBAN_POSTGRES_CONNECTION_STRING: z.string().optional(),
  SEKIBAN_POSTGRES_POOL_MIN: z.string().transform(Number).pipe(z.number().int().min(0)).optional(),
  SEKIBAN_POSTGRES_POOL_MAX: z.string().transform(Number).pipe(z.number().int().positive()).optional(),
  
  // CosmosDB
  SEKIBAN_COSMOS_ENDPOINT: z.string().url().optional(),
  SEKIBAN_COSMOS_KEY: z.string().optional(),
  SEKIBAN_COSMOS_DATABASE_ID: z.string().optional(),
  SEKIBAN_COSMOS_CONTAINER_ID: z.string().optional(),
  SEKIBAN_COSMOS_CONSISTENCY_LEVEL: z.enum(['Strong', 'BoundedStaleness', 'Session', 'ConsistentPrefix', 'Eventual']).optional(),
  
  // Common settings
  SEKIBAN_DEFAULT_PARTITION_KEY: z.string().optional(),
  SEKIBAN_ENABLE_METRICS: z.string().transform(v => v === 'true').optional(),
  SEKIBAN_ENABLE_TRACING: z.string().transform(v => v === 'true').optional(),
  SEKIBAN_LOG_LEVEL: z.enum(['debug', 'info', 'warn', 'error']).optional(),
  
  // Retry settings
  SEKIBAN_MAX_RETRIES: z.string().transform(Number).pipe(z.number().int().positive()).optional(),
  SEKIBAN_RETRY_DELAY: z.string().transform(Number).pipe(z.number().int().positive()).optional(),
  SEKIBAN_TIMEOUT: z.string().transform(Number).pipe(z.number().int().positive()).optional()
})

// Type exports
export type InMemoryConfig = z.infer<typeof inMemoryConfigSchema>
export type PostgresConfig = z.infer<typeof postgresConfigSchema>
export type CosmosConfig = z.infer<typeof cosmosConfigSchema>
export type StorageConfig = z.infer<typeof storageConfigSchema>
export type SekibanConfig = z.infer<typeof sekibanConfigSchema>
export type EnvConfig = z.infer<typeof envSchema>