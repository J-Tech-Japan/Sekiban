import dotenv from 'dotenv';

// Load environment variables
dotenv.config();

interface Config {
  NODE_ENV: string;
  PORT: number;
  DAPR_HTTP_PORT: number;
  DAPR_APP_ID: string;
  API_PREFIX: string;
  CORS_ORIGIN: string;
  STORAGE_TYPE: 'postgres' | 'cosmos';
  DATABASE_URL: string;
  COSMOS_CONNECTION_STRING?: string;
  COSMOS_DATABASE?: string;
  COSMOS_CONTAINER?: string;
}

export const config: Config = {
  NODE_ENV: process.env.NODE_ENV || 'development',
  PORT: parseInt(process.env.PORT || '3002', 10),
  DAPR_HTTP_PORT: parseInt(process.env.DAPR_HTTP_PORT || '3503', 10),
  DAPR_APP_ID: process.env.DAPR_APP_ID || 'dapr-sample-api-multi-projector',
  API_PREFIX: process.env.API_PREFIX || '/api/v1',
  CORS_ORIGIN: process.env.CORS_ORIGIN || '*',
  STORAGE_TYPE: (process.env.STORAGE_TYPE as 'postgres' | 'cosmos') || 'postgres',
  DATABASE_URL: process.env.DATABASE_URL || (() => {
    // WARNING: Default credentials for development only - NEVER use in production
    if (process.env.NODE_ENV === 'production') {
      throw new Error('DATABASE_URL must be explicitly set in production environment');
    }
    return 'postgresql://sekiban:sekiban_password@localhost:5432/sekiban_events';
  })(),
  COSMOS_CONNECTION_STRING: process.env.COSMOS_CONNECTION_STRING,
  COSMOS_DATABASE: process.env.COSMOS_DATABASE || 'sekiban-events',
  COSMOS_CONTAINER: process.env.COSMOS_CONTAINER || 'events'
};