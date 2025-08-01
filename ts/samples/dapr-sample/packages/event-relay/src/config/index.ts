import dotenv from 'dotenv';

// Load environment variables
dotenv.config();

export const config = {
  // Server
  NODE_ENV: process.env.NODE_ENV || 'development',
  PORT: parseInt(process.env.PORT || '3003', 10),
  
  // Dapr
  DAPR_APP_ID: process.env.DAPR_APP_ID || 'dapr-sample-event-relay',
  DAPR_HTTP_PORT: parseInt(process.env.DAPR_HTTP_PORT || '3503', 10),
  DAPR_GRPC_PORT: parseInt(process.env.DAPR_GRPC_PORT || '50003', 10),
  
  // Database
  USE_POSTGRES: process.env.USE_POSTGRES === 'true',
  DATABASE_URL: process.env.DATABASE_URL || 'postgresql://sekiban:sekiban_password@localhost:5432/sekiban_events',
} as const;