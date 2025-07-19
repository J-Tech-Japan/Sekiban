import { DaprClient } from '@dapr/dapr';
import type { DaprSekibanConfiguration, ISekibanDaprExecutor } from '@sekiban/dapr';
import type { ICommandWithHandler } from '@sekiban/core';
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

// Create a wrapper that adds logging
class LoggingSekibanExecutor implements SekibanExecutor {
  constructor(private readonly innerExecutor: ISekibanDaprExecutor) {}

  async executeCommandAsync<TCommand extends ICommandWithHandler<any, any, any>>(
    command: TCommand,
    commandData?: any,
    metadata?: any
  ): Promise<any> {
    const startTime = Date.now();
    const commandName = (command as any).commandType || (command as any).constructor?.name || 'UnknownCommand';
    
    console.log(`[EXECUTOR] Starting command execution: ${commandName}`);
    console.log(`[EXECUTOR] Command data:`, JSON.stringify(commandData || command, null, 2));
    console.log(`[EXECUTOR] Metadata:`, JSON.stringify(metadata, null, 2));
    
    try {
      const result = await this.innerExecutor.executeCommandAsync(command, commandData, metadata);
      
      const duration = Date.now() - startTime;
      console.log(`[EXECUTOR] Command ${commandName} completed in ${duration}ms`);
      console.log(`[EXECUTOR] Result:`, JSON.stringify(result, null, 2));
      
      return result;
    } catch (error) {
      const duration = Date.now() - startTime;
      console.error(`[EXECUTOR] Command ${commandName} failed after ${duration}ms:`, error);
      throw error;
    }
  }

  async queryAsync<TQuery>(query: TQuery): Promise<any> {
    const startTime = Date.now();
    const queryName = (query as any).queryType || (query as any).constructor?.name || 'UnknownQuery';
    
    console.log(`[EXECUTOR] Starting query execution: ${queryName}`);
    console.log(`[EXECUTOR] Query data:`, JSON.stringify(query, null, 2));
    
    try {
      const result = await this.innerExecutor.queryAsync(query);
      
      const duration = Date.now() - startTime;
      console.log(`[EXECUTOR] Query ${queryName} completed in ${duration}ms`);
      console.log(`[EXECUTOR] Result:`, JSON.stringify(result, null, 2));
      
      return result;
    } catch (error) {
      const duration = Date.now() - startTime;
      console.error(`[EXECUTOR] Query ${queryName} failed after ${duration}ms:`, error);
      throw error;
    }
  }
}

let executorInstance: SekibanExecutor | null = null;
let daprClientInstance: DaprClient | null = null;

/**
 * Create a Sekiban executor with:
 * - Dapr for state management (in-memory state store)
 * - Dapr for pub/sub (in-memory pubsub)
 * - Note: PostgreSQL event store integration will be configured separately
 */
export async function createExecutor(): Promise<SekibanExecutor> {
  if (executorInstance) {
    console.log('[EXECUTOR SETUP] Returning existing executor instance');
    return executorInstance;
  }

  console.log('[EXECUTOR SETUP] Creating new executor instance...');

  // Initialize domain types
  console.log('[EXECUTOR SETUP] Initializing domain types...');
  const domainTypes = createTaskDomainTypes();

  // Check if we should use Dapr
  const useDapr = process.env.DAPR_HTTP_PORT && process.env.DAPR_HTTP_PORT !== '';
  
  if (!useDapr) {
    console.error('[EXECUTOR SETUP] ERROR: Dapr is required for this sample.');
    console.error('[EXECUTOR SETUP] Please run with Dapr using: dapr run --app-id sekiban-api --dapr-http-port 3500 --app-port 3000 -- npm run start');
    throw new Error('Dapr not available. This sample requires Dapr to run.');
  }

  console.log('[EXECUTOR SETUP] Initializing Sekiban with Dapr...');

  // Create Dapr client with proper configuration
  console.log('[EXECUTOR SETUP] Creating DaprClient with config:', {
    daprHost: '127.0.0.1',
    daprPort: String(config.DAPR_HTTP_PORT)
  });
  
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
    retryAttempts: 3,
    retryDelayMs: 100
  };

  console.log('[EXECUTOR SETUP] Sekiban configuration:', JSON.stringify(sekibanConfig, null, 2));

  // Dynamically import SekibanDaprExecutor
  const { SekibanDaprExecutor } = await import('@sekiban/dapr');
  
  // Create the Sekiban Dapr executor
  console.log('[EXECUTOR SETUP] Creating SekibanDaprExecutor...');
  const innerExecutor = new SekibanDaprExecutor(
    daprClientInstance,
    domainTypes,
    sekibanConfig
  );

  // Wrap with logging executor
  executorInstance = new LoggingSekibanExecutor(innerExecutor);

  console.log('[EXECUTOR SETUP] Sekiban Dapr Executor initialized with config:', {
    actorType: sekibanConfig.actorType,
    actorIdPrefix: sekibanConfig.actorIdPrefix,
    stateStore: sekibanConfig.stateStoreName + ' (in-memory)',
    pubSub: sekibanConfig.pubSubName + ' (in-memory)',
    eventTopic: sekibanConfig.eventTopicName
  });

  return executorInstance!;
}

export async function getExecutor(): Promise<SekibanExecutor> {
  if (!executorInstance) {
    console.log('[EXECUTOR] No executor instance found, creating new one...');
    return createExecutor();
  }
  console.log('[EXECUTOR] Returning existing executor instance');
  return executorInstance;
}

export function getDaprClient(): DaprClient {
  if (!daprClientInstance) {
    console.log('[DAPR CLIENT] Creating new DaprClient instance...');
    daprClientInstance = new DaprClient({
      daprHost: '127.0.0.1',
      daprPort: String(config.DAPR_HTTP_PORT)
    });
  }
  return daprClientInstance;
}

// Cleanup function for graceful shutdown
export async function cleanup(): Promise<void> {
  console.log('[CLEANUP] Cleanup completed');
}