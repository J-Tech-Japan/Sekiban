import express from 'express';
import { DaprServer, CommunicationProtocolEnum, DaprClient } from '@dapr/dapr';
import { AggregateEventHandlerActorFactory, AggregateEventHandlerActor, initializeDaprContainer } from '@sekiban/dapr';
// CLAUDE.md: InMemoryEventStore removed - only proper storage implementations allowed
// Dynamic imports for storage providers to avoid module resolution errors
import { CosmosEventStore } from '@sekiban/cosmos';
// Import domain types at the top to ensure registration happens
import { createTaskDomainTypes } from '@dapr-sample/domain';
import pg from 'pg';
import pino from 'pino';
// Use environment variables directly

const { Pool } = pg;

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
const DAPR_HTTP_PORT = process.env.DAPR_HTTP_PORT || 3502;
const STORAGE_TYPE = process.env.STORAGE_TYPE || 'postgres';
const DATABASE_URL = process.env.DATABASE_URL || 'postgresql://sekiban:sekiban_password@localhost:5432/sekiban_events';
const COSMOS_CONNECTION_STRING = process.env.COSMOS_CONNECTION_STRING || '';
const COSMOS_DATABASE = process.env.COSMOS_DATABASE || 'sekiban-events';
const COSMOS_CONTAINER = process.env.COSMOS_CONTAINER || 'events';

// Log configuration on startup
logger.info('=== Event Handler Configuration ===');
logger.info(`STORAGE_TYPE: ${STORAGE_TYPE}`);
logger.info(`DATABASE_URL: ${DATABASE_URL ? '[CONFIGURED]' : '[NOT SET]'}`);
logger.info(`COSMOS_CONNECTION_STRING: ${COSMOS_CONNECTION_STRING ? '[CONFIGURED]' : '[NOT SET]'}`);
logger.info(`PORT: ${PORT}`);
logger.info(`DAPR_HTTP_PORT: ${DAPR_HTTP_PORT}`);
logger.info('==================================');

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
      // Dynamic import for PostgreSQL
      const { PostgresEventStore } = await import('@sekiban/postgres');
      
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
      cleanup = async () => {
        await pool.end();
        logger.info('PostgreSQL connection closed');
      };
      break;
    }
    
    case 'cosmos': {
      logger.info('Using Cosmos DB event store');
      
      if (!COSMOS_CONNECTION_STRING) {
        logger.error('COSMOS_CONNECTION_STRING environment variable is required for Cosmos DB storage');
        throw new Error('COSMOS_CONNECTION_STRING is required');
      }
      
      // Import CosmosClient from Azure SDK
      const { CosmosClient } = await import('@azure/cosmos');
      
      // Create Cosmos client with connection string directly
      const cosmosClient = new CosmosClient(COSMOS_CONNECTION_STRING);
      
      // Create database if it doesn't exist
      const { database } = await cosmosClient.databases.createIfNotExists({
        id: COSMOS_DATABASE
      });
      
      logger.info(`Cosmos DB database '${COSMOS_DATABASE}' ready`);
      
      // Create event store with the database object
      eventStore = new CosmosEventStore(database);
      
      // Initialize the event store (creates containers)
      const result = await eventStore.initialize();
      if (result.isOk()) {
        logger.info('Cosmos DB event store initialized successfully');
      } else {
        logger.error('Failed to initialize Cosmos DB event store:', result.error);
        throw result.error;
      }
      
      cleanup = async () => {
        logger.info('Cosmos DB cleanup completed');
      };
      break;
    }
    
    default: {
      throw new Error(`CLAUDE.md violation: Storage type '${STORAGE_TYPE}' not supported. Use 'postgres' or 'cosmos' for proper actor implementation. In-memory workarounds are forbidden.`);
    }
  }

  // Initialize domain types
  const domainTypes = createTaskDomainTypes();
  
  // Initialize DaprContainer with necessary dependencies
  initializeDaprContainer({
    domainTypes: domainTypes,
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