import type { Application } from 'express';
import { createApp } from '../../src/app';
import { createTestDatabase } from './test-database';
import { createTestConfig } from './test-config';
import type { MockDaprClient } from './mock-dapr-client';

export interface BootAppOptions {
  daprClient?: MockDaprClient;
}

export interface BootAppResult {
  app: Application;
  cleanup: () => Promise<void>;
}

export async function bootApp(options: BootAppOptions = {}): Promise<BootAppResult> {
  // Create test database
  const { connectionString, cleanup: dbCleanup } = await createTestDatabase();
  
  // Create test configuration
  const config = createTestConfig(connectionString);
  
  // Create Express app with test dependencies
  const app = await createApp(config, {
    daprClient: options.daprClient
  });
  
  const cleanup = async () => {
    await dbCleanup();
  };
  
  return { app, cleanup };
}