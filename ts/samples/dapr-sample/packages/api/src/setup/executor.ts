import { DaprClient } from '@dapr/dapr';
import { SekibanDaprExecutor } from '@sekiban/dapr';
import { createPostgresEventStore } from '@sekiban/postgres';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import { config } from '../config/index.js';

let executorInstance: SekibanDaprExecutor | null = null;

export async function createExecutor(): Promise<SekibanDaprExecutor> {
  if (executorInstance) {
    return executorInstance;
  }

  // Create Dapr client
  const daprClient = new DaprClient({
    daprHost: '127.0.0.1',
    daprPort: config.DAPR_HTTP_PORT.toString()
  });

  // Create domain types
  const domainTypes = createTaskDomainTypes();

  // Create PostgreSQL event store
  const eventStore = await createPostgresEventStore({
    connectionString: config.DATABASE_URL,
    domainTypes
  });

  // Create Dapr executor with PostgreSQL storage
  executorInstance = new SekibanDaprExecutor(
    daprClient,
    domainTypes,
    {
      stateStoreName: config.DAPR_STATE_STORE_NAME,
      pubSubName: config.DAPR_PUBSUB_NAME,
      eventTopicName: config.DAPR_EVENT_TOPIC,
      actorType: config.DAPR_ACTOR_TYPE,
      actorIdPrefix: config.DAPR_APP_ID,
      storageProvider: eventStore
    }
  );

  return executorInstance;
}

export async function getExecutor(): Promise<SekibanDaprExecutor> {
  if (!executorInstance) {
    return createExecutor();
  }
  return executorInstance;
}