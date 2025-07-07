// import { DaprClient } from '@dapr/dapr';
// import { SekibanDaprExecutor } from '@sekiban/dapr';
import { createPostgresEventStore, PostgresEventStore } from '@sekiban/postgres';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import { config } from '../config/index.js';
import { ISekibanExecutor } from '@sekiban/core';
import { Pool } from 'pg';

let executorInstance: ISekibanExecutor | null = null;
let eventStoreInstance: PostgresEventStore | null = null;

export async function createExecutor(): Promise<ISekibanExecutor> {
  if (executorInstance) {
    return executorInstance;
  }

  // Create domain types
  const domainTypes = createTaskDomainTypes();

  // Create PostgreSQL connection pool
  const pool = new Pool({
    connectionString: config.DATABASE_URL,
    max: 20,
    idleTimeoutMillis: 30000,
    connectionTimeoutMillis: 2000,
  });

  // Create PostgreSQL event store
  eventStoreInstance = new PostgresEventStore(pool);
  
  // Initialize the database schema
  await eventStoreInstance.initialize();

  // Create a basic executor using the event store
  executorInstance = {
    executeCommand: async (command: any) => {
      // Basic command execution would go here
      throw new Error('Command execution not yet implemented');
    },
    executeQuery: async (query: any) => {
      // Basic query execution would go here
      throw new Error('Query execution not yet implemented');
    }
  } as ISekibanExecutor;

  return executorInstance;
}

export async function getExecutor(): Promise<ISekibanExecutor> {
  if (!executorInstance) {
    return createExecutor();
  }
  return executorInstance;
}

export async function getEventStore(): Promise<PostgresEventStore> {
  if (!eventStoreInstance) {
    await createExecutor();
  }
  return eventStoreInstance!;
}