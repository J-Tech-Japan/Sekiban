import { z } from 'zod';
import dotenv from 'dotenv';
import path from 'path';
import { fileURLToPath } from 'url';

// Get __dirname equivalent in ES modules
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Load .env from project root
const envPath = path.resolve(__dirname, '../../../.env');
const dotenvResult = dotenv.config({ path: envPath });
// .env is optional, so we don't log errors if it's missing

const ConfigSchema = z.object({
  NODE_ENV: z.enum(['development', 'test', 'production']).default('development'),
  PORT: z.string().default('3000').transform(Number),
  ACTOR_SERVER_PORT: z.string().default('50010').transform(Number),
  
  // Storage configuration
  STORAGE_TYPE: z.enum(['inmemory', 'postgres', 'cosmos']).default('inmemory'),
  
  // PostgreSQL configuration
  DATABASE_URL: z.string().default('postgresql://sekiban:sekiban_password@localhost:5432/sekiban_events'),
  
  // Cosmos DB configuration
  COSMOS_CONNECTION_STRING: z.string().optional(),
  COSMOS_DATABASE: z.string().default('sekiban-events'),
  COSMOS_CONTAINER: z.string().default('events'),
  
  
  // Dapr configuration
  DAPR_HTTP_PORT: z.string().default('3500').transform(Number),
  DAPR_GRPC_PORT: z.string().default('50001').transform(Number),
  DAPR_APP_ID: z.string().default('sekiban-api'),
  DAPR_STATE_STORE_NAME: z.string().default('statestore'),
  DAPR_PUBSUB_NAME: z.string().default('pubsub'),
  DAPR_EVENT_TOPIC: z.string().default('events'),
  
  // Actor configuration - must match Sekiban's internal actor names
  DAPR_ACTOR_TYPE: z.string().default('AggregateActor'),
  DAPR_APP_ID_FOR_ACTORS: z.string().default('sekiban-api'), // App ID where actors are hosted
  
  // API configuration
  API_PREFIX: z.string().default('/api'),
  CORS_ORIGIN: z.string().default('*'),
  
  // Logging
  LOG_LEVEL: z.enum(['error', 'warn', 'info', 'debug']).default('info')
});

const configResult = ConfigSchema.safeParse(process.env);

if (!configResult.success) {
  console.error('Invalid configuration:', configResult.error.format());
  process.exit(1);
}

export const config = configResult.data;

export type Config = typeof config;