import { DaprClient } from '@dapr/dapr';
import type { DaprSekibanConfiguration, ISekibanDaprExecutor } from '@sekiban/dapr';
import type { ICommandWithHandler, IEventStore } from '@sekiban/core';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import { config } from '../config/index.js';

// Common interface for both executors
interface SekibanExecutor {
  executeCommandAsync<TCommand extends ICommandWithHandler<any, any, any>>(
    command: TCommand,
    commandData?: any,
    metadata?: any
  ): Promise<any>;
  queryAsync<TQuery>(
    query: TQuery
  ): Promise<any>;
}

let executorInstance: SekibanExecutor | null = null;
let daprClientInstance: DaprClient | null = null;
let eventStoreInstance: IEventStore | null = null;

/**
 * Set the event store to be used by the executor
 */
export function setEventStore(eventStore: IEventStore): void {
  eventStoreInstance = eventStore;
}

/**
 * Get the configured event store
 */
export function getEventStore(): IEventStore | null {
  return eventStoreInstance;
}

/**
 * Create a Sekiban executor with:
 * - Dapr for state management (in-memory state store)
 * - Dapr for pub/sub (in-memory pubsub)
 * - Event store passed from server initialization
 */
export async function createExecutor(): Promise<SekibanExecutor> {
  if (executorInstance) {
    return executorInstance;
  }

  // Initialize domain types
  const domainTypes = createTaskDomainTypes();

  // Check if we should use Dapr
  const useDapr = process.env.DAPR_HTTP_PORT && process.env.DAPR_HTTP_PORT !== '';
  
  if (!useDapr) {
    console.error('ERROR: Dapr is required for this sample.');
    console.error('Please run with Dapr using: dapr run --app-id sekiban-api --dapr-http-port 3500 --app-port 3000 -- npm run start');
    throw new Error('Dapr not available. This sample requires Dapr to run.');
  }

  // Create Dapr client with proper configuration
  daprClientInstance = new DaprClient({
    daprHost: '127.0.0.1',
    daprPort: String(config.DAPR_HTTP_PORT)
  });

  // Configure Sekiban with Dapr - using in-memory state store and pubsub
  const sekibanConfig: DaprSekibanConfiguration = {
    stateStoreName: config.DAPR_STATE_STORE_NAME,
    pubSubName: config.DAPR_PUBSUB_NAME,
    eventTopicName: config.DAPR_EVENT_TOPIC,
    actorType: config.DAPR_ACTOR_TYPE,
    actorIdPrefix: config.DAPR_APP_ID,
    retryAttempts: 3
  };

  // Dynamically import SekibanDaprExecutor
  const { SekibanDaprExecutor } = await import('@sekiban/dapr');
  
  // Create the Sekiban Dapr executor
  executorInstance = new SekibanDaprExecutor(
    daprClientInstance,
    domainTypes,
    sekibanConfig
  ) as any; // Cast to match our interface

  return executorInstance!; // We know it's initialized at this point
}

export async function getExecutor(): Promise<SekibanExecutor> {
  if (!executorInstance) {
    return createExecutor();
  }
  return executorInstance;
}

export function getDaprClient(): DaprClient {
  if (!daprClientInstance) {
    daprClientInstance = new DaprClient({
      daprHost: '127.0.0.1',
      daprPort: String(config.DAPR_HTTP_PORT)
    });
  }
  return daprClientInstance;
}

// Cleanup function for graceful shutdown
export async function cleanup(): Promise<void> {
  // Add any cleanup logic if needed
}