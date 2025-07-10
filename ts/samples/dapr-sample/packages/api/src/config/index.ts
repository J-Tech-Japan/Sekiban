import { z } from 'zod';
import dotenv from 'dotenv';

dotenv.config();

const ConfigSchema = z.object({
  NODE_ENV: z.enum(['development', 'test', 'production']).default('development'),
  PORT: z.string().default('3000').transform(Number),
  ACTOR_SERVER_PORT: z.string().default('50010').transform(Number),
  
  // PostgreSQL configuration
  DATABASE_URL: z.string().default('postgresql://sekiban:sekiban_password@localhost:5432/sekiban_events'),
  
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