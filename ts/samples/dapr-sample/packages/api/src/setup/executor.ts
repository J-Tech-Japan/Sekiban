import { DaprClient } from '@dapr/dapr';
import { SekibanDaprExecutor, type DaprSekibanConfiguration } from '@sekiban/dapr';
import type { ICommandWithHandler } from '@sekiban/core';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import { config } from '../config/index.js';

// Common interface for both executors
interface SekibanExecutor {
  executeCommandAsync<TCommand extends ICommandWithHandler<any, any, any>>(
    command: TCommand
  ): Promise<any>;
  queryAsync<TQuery>(
    query: TQuery
  ): Promise<any>;
}

let executorInstance: SekibanExecutor | null = null;
let daprClientInstance: DaprClient | null = null;

/**
 * Create a Sekiban executor based on environment configuration
 * - If DAPR_HTTP_PORT is not set or Dapr is not available, uses in-memory
 * - Otherwise uses Dapr executor for distributed execution
 */
export async function createExecutor(): Promise<SekibanExecutor> {
  if (executorInstance) {
    return executorInstance;
  }

  // Initialize domain types
  const domainTypes = createTaskDomainTypes();

  // Check if we should use Dapr
  const useDapr = process.env.DAPR_HTTP_PORT && process.env.DAPR_HTTP_PORT !== '';
  
  if (useDapr) {
    console.log('Using Dapr executor for distributed event sourcing...');
    
    // Create Dapr client with proper configuration
    daprClientInstance = new DaprClient({
      daprHost: '127.0.0.1',
      daprPort: String(config.DAPR_HTTP_PORT)
    });

    // Configure Sekiban with Dapr - include all required configuration
    const sekibanConfig: DaprSekibanConfiguration = {
      stateStoreName: config.DAPR_STATE_STORE_NAME,
      pubSubName: config.DAPR_PUBSUB_NAME,
      eventTopicName: config.DAPR_EVENT_TOPIC,
      actorType: config.DAPR_ACTOR_TYPE,
      actorIdPrefix: config.DAPR_APP_ID,
      retryAttempts: 3,
      retryDelayMs: 100
    };

    // Create the Sekiban Dapr executor
    executorInstance = new SekibanDaprExecutor(
      daprClientInstance,
      domainTypes,
      sekibanConfig
    );

    console.log('Sekiban Dapr Executor initialized with config:', {
      actorType: sekibanConfig.actorType,
      actorIdPrefix: sekibanConfig.actorIdPrefix,
      stateStore: sekibanConfig.stateStoreName,
      pubSub: sekibanConfig.pubSubName,
      eventTopic: sekibanConfig.eventTopicName
    });
  } else {
    console.log('ERROR: Dapr is required for this sample.');
    console.log('Please run with Dapr using: ./run-with-dapr.sh');
    throw new Error('Dapr not available. This sample requires Dapr to run.');
  }

  return executorInstance!; // We know it's initialized by this point
}

export async function getExecutor(): Promise<SekibanExecutor> {
  if (!executorInstance) {
    return createExecutor();
  }
  return executorInstance;
}

export function getDaprClient(): DaprClient {
  if (!daprClientInstance) {
    daprClientInstance = new DaprClient();
  }
  return daprClientInstance;
}