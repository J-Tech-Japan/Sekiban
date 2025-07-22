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
  STORAGE_TYPE: 'inmemory' | 'postgres';
  DATABASE_URL: string;
}

export const config: Config = {
  NODE_ENV: process.env.NODE_ENV || 'development',
  PORT: parseInt(process.env.PORT || '3003', 10),
  DAPR_HTTP_PORT: parseInt(process.env.DAPR_HTTP_PORT || '3503', 10),
  DAPR_APP_ID: process.env.DAPR_APP_ID || 'dapr-sample-api-multi-projector',
  API_PREFIX: process.env.API_PREFIX || '/api/v1',
  CORS_ORIGIN: process.env.CORS_ORIGIN || '*',
  STORAGE_TYPE: (process.env.STORAGE_TYPE as 'inmemory' | 'postgres') || 'inmemory',
  DATABASE_URL: process.env.DATABASE_URL || 'postgresql://sekiban:sekiban_password@localhost:5432/sekiban_events'
};