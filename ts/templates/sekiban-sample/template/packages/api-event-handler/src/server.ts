import express from 'express';
import { DaprServer, CommunicationProtocolEnum, DaprClient } from '@dapr/dapr';
import { AggregateEventHandlerActorFactory, AggregateEventHandlerActor, initializeDaprContainer } from '@sekiban/dapr';
import { InMemoryEventStore, StorageProviderType } from '@sekiban/core';
import { PostgresEventStore } from '@sekiban/postgres';
import { createCosmosEventStore } from '@sekiban/cosmos';
import { Pool } from 'pg';
import { pino } from 'pino';

const logger = pino({
  transport: {
    target: 'pino-pretty',
    options: {
      colorize: true,
      translateTime: 'HH:MM:ss Z',
      ignore: 'pid,hostname'
    }
  }
});

const PORT = process.env.PORT || 3001;
const DAPR_HTTP_PORT = process.env.DAPR_HTTP_PORT || 3501;
const STORAGE_TYPE = process.env.STORAGE_TYPE || 'inmemory';
const DATABASE_URL = process.env.DATABASE_URL || 'postgresql://sekiban:sekiban_password@localhost:5432/sekiban_events';
const COSMOS_CONNECTION_STRING = process.env.COSMOS_CONNECTION_STRING || '';
const COSMOS_DATABASE = process.env.COSMOS_DATABASE || 'sekiban-events';
const COSMOS_CONTAINER = process.env.COSMOS_CONTAINER || 'events';

async function main() {
  const app = express();
  app.use(express.json());

  // Health check endpoint
  app.get('/health', (req, res) => {
    res.json({ status: 'healthy', service: 'api-event-handler' });
  });

  // Initialize event store
  let eventStore: any;
  let cleanup: () => Promise<void> = async () => {};

  switch (STORAGE_TYPE) {
    case 'postgres': {
      logger.info('Using PostgreSQL event store');
      logger.info('Database URL:', DATABASE_URL);
      const pool = new Pool({ connectionString: DATABASE_URL });
      
      // Test connection
      try {
        await pool.query('SELECT 1');
        logger.info('PostgreSQL connection successful');
      } catch (error) {
        logger.error('Failed to connect to PostgreSQL:', error);
        throw error;
      }
      
      eventStore = new PostgresEventStore(pool);
      
      // Initialize the database schema
      try {
        const result = await eventStore.initialize();
        if (!result.isOk()) {
          logger.error('Failed to initialize PostgreSQL schema:', result.error);
          throw result.error;
        }
        logger.info('PostgreSQL event store initialized successfully');
      } catch (error) {
        logger.error('Failed to initialize PostgreSQL:', error);
        throw error;
      }
      
      cleanup = async () => {
        await pool.end();
        logger.info('PostgreSQL connection closed');
      };
      break;
    }
    
    case 'cosmos': {
      logger.info('Using Cosmos DB event store');
      if (!COSMOS_CONNECTION_STRING) {
        throw new Error('COSMOS_CONNECTION_STRING environment variable is required for Cosmos DB storage');
      }
      
      // Use createCosmosEventStore helper function
      const result = await createCosmosEventStore({
        type: StorageProviderType.CosmosDB,
        connectionString: COSMOS_CONNECTION_STRING,
        databaseName: COSMOS_DATABASE,
        enableLogging: true
      });
      
      if (!result.isOk()) {
        logger.error('Failed to create Cosmos DB event store:', result.error);
        throw result.error;
      }
      
      eventStore = result.value;
      logger.info('Cosmos DB event store created successfully');
      break;
    }
    
    case 'inmemory':
    default: {
      logger.info('Using in-memory event store');
      eventStore = new InMemoryEventStore({
        type: StorageProviderType.InMemory,
        enableLogging: true
      });
      break;
    }
  }

  // Initialize DaprContainer with necessary dependencies
  initializeDaprContainer({
    domainTypes: {} as any, // Event handler doesn't need domain types
    serviceProvider: {},
    actorProxyFactory: {
      createActorProxy: <T>(): T => {
        throw new Error('Event handler does not create other actors');
      }
    },
    serializationService: {},
    eventStore: eventStore
  });
  
  // Configure AggregateEventHandlerActor factory
  (AggregateEventHandlerActorFactory as any).configure(eventStore);

  // Create actor class
  const EventHandlerActorClass = (AggregateEventHandlerActorFactory as any).createActorClass();
  logger.info('EventHandlerActorClass created:', EventHandlerActorClass.name);

  // Create DaprServer
  const daprServer = new DaprServer({
    serverHost: "127.0.0.1",
    serverPort: String(PORT),
    serverHttp: app,
    communicationProtocol: CommunicationProtocolEnum.HTTP,
    clientOptions: {
      daprHost: "127.0.0.1",
      daprPort: String(DAPR_HTTP_PORT),
      communicationProtocol: CommunicationProtocolEnum.HTTP
    }
  });

  // Register AggregateEventHandlerActor
  await daprServer.actor.registerActor(EventHandlerActorClass);
  logger.info('Registered AggregateEventHandlerActor');

  // Initialize actor runtime
  logger.info('Initializing actor runtime...');
  await daprServer.actor.init();
  logger.info('Actor runtime initialized');

  // Start the server
  await daprServer.start();
  logger.info(`🚀 Event Handler Service is running on port ${PORT}`);
  logger.info(`🎭 Actor registered: AggregateEventHandlerActor`);
  logger.info(`🔗 Dapr HTTP port: ${DAPR_HTTP_PORT}`);

  // Graceful shutdown
  const gracefulShutdown = async (signal: string) => {
    logger.info(`${signal} received, shutting down gracefully...`);
    
    const forceExitTimeout = setTimeout(() => {
      logger.error('Forced shutdown after timeout');
      process.exit(1);
    }, 30000);

    try {
      await daprServer.stop();
      logger.info('DaprServer stopped');
      await cleanup();
      logger.info('Cleanup completed');
      clearTimeout(forceExitTimeout);
    } catch (error) {
      logger.error('Error during shutdown:', error);
    }
    
    process.exit(0);
  };

  process.on('SIGTERM', () => gracefulShutdown('SIGTERM'));
  process.on('SIGINT', () => gracefulShutdown('SIGINT'));
}

main().catch((error) => {
  logger.error('Failed to start server:', error);
  process.exit(1);
});