import express from 'express';
import cors from 'cors';
import helmet from 'helmet';
import compression from 'compression';
import morgan from 'morgan';
import pg from 'pg';
import { DaprServer, DaprClient, CommunicationProtocolEnum, HttpMethod, ActorProxyBuilder, ActorId } from '@dapr/dapr';
import { MultiProjectorActorFactory, getDaprCradle, MultiProjectorActor } from '@sekiban/dapr';
import { IEventStore } from '@sekiban/core';
// Dynamic imports for storage providers to avoid module resolution errors
import { CosmosEventStore } from '@sekiban/cosmos';
import { CosmosClient } from '@azure/cosmos';
// Import domain types at the top to ensure registration happens
import '@dapr-sample/domain';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import { config } from './config/index.js';
import { errorHandler } from './middleware/error-handler.js';
import { healthRoutes } from './routes/health-routes.js';
import { multiProjectorRoutes } from './routes/multi-projector-routes.js';
import logger from './utils/logger.js';
import type { MultiProjectorActorFactoryConfigureMethod, MultiProjectorActorFactoryCreateMethod } from './types/factory-types.js';
import type { PubSubEventData, SerializationService, ActorProxyFactory } from './types/domain-types.js';

const { Pool } = pg;

async function startServer() {
  const app = express();

  // Middleware
  app.use(helmet());
  app.use(cors({ origin: config.CORS_ORIGIN }));
  app.use(compression());
  // Custom morgan format to skip eventCheck reminders
  app.use(morgan((tokens, req, res) => {
    // Skip logging for eventCheck reminders
    if (req.url?.includes('/remind/eventCheck')) {
      return null;
    }
    // Use default dev format for other requests
    return [
      tokens.method(req, res),
      tokens.url(req, res),
      tokens.status(req, res),
      tokens.res(req, res, 'content-length'), '-',
      tokens['response-time'](req, res), 'ms'
    ].join(' ');
  }));
  app.use(express.json());
  app.use(express.urlencoded({ extended: true }));

  // Debug middleware to log all requests
  app.use((req, res, next) => {
    // Skip logging for eventCheck reminders and health checks
    if (!req.path.includes('/remind/eventCheck') && !req.path.includes('/health')) {
      logger.debug(`[${new Date().toISOString()}] ${req.method} ${req.path}`);
    }
    next();
  });

  // CRITICAL: Convert POST to PUT for actor method calls
  app.use((req, res, next) => {
    if (req.path.includes('/method/') && req.method === 'POST') {
      req.method = 'PUT';
    }
    next();
  });
  
  // Routes
  app.use('/', healthRoutes);
  app.use(config.API_PREFIX, multiProjectorRoutes);

  // Error handling (must be last)
  app.use(errorHandler);

  // Create DaprServer and pass the Express app
  const daprServer = await setupDaprActorsWithApp(app);

  // Start the DaprServer
  await daprServer.start();
  
  console.log(`
🚀 MultiProjector Service is running!
📡 Environment: ${config.NODE_ENV}
🔗 URL: http://localhost:${config.PORT}
🔗 API: http://localhost:${config.PORT}${config.API_PREFIX}
🎭 Dapr App ID: ${config.DAPR_APP_ID}
🎭 Actors: MultiProjectorActor
  `);

  // Graceful shutdown
  const gracefulShutdown = async (signal: string) => {
    console.log(`\n${signal} received, starting graceful shutdown...`);
    
    const forceExitTimeout = setTimeout(() => {
      console.error('Forced shutdown after timeout');
      process.exit(1);
    }, 30000);
    
    try {
      await daprServer.stop();
      console.log('DaprServer stopped');
      
      clearTimeout(forceExitTimeout);
    } catch (error) {
      console.error('Error during cleanup:', error);
    }
    
    process.exit(0);
  };

  process.on('SIGTERM', () => gracefulShutdown('SIGTERM'));
  process.on('SIGINT', () => gracefulShutdown('SIGINT'));
}


async function setupDaprActorsWithApp(app: express.Express) {

  // Initialize domain types
  const domainTypes = createTaskDomainTypes();

  // CLAUDE.md: Never create in-memory workarounds - always use proper actor implementation
  let eventStore: IEventStore;
  
  switch (config.STORAGE_TYPE) {
    case 'postgres': {
      // Dynamic import for PostgreSQL
      const { PostgresEventStore } = await import('@sekiban/postgres');
      
      // Initialize PostgreSQL event store
      const pool = new Pool({
        connectionString: config.DATABASE_URL
      });
      
      eventStore = new PostgresEventStore(pool);
      
      // Initialize the database schema
      try {
        const result = await eventStore.initialize();
        if (result.isErr()) {
          logger.error('Failed to initialize PostgreSQL schema:', result.error);
          throw result.error;
        }
      } catch (error) {
        logger.error('Failed to initialize PostgreSQL:', error);
        throw error;
      }
      break;
    }
    
    case 'cosmos': {
      // Initialize Cosmos DB event store
      if (!config.COSMOS_CONNECTION_STRING) {
        throw new Error('COSMOS_CONNECTION_STRING is required when using cosmos storage');
      }
      
      const cosmosClient = new CosmosClient(config.COSMOS_CONNECTION_STRING);
      const database = cosmosClient.database(config.COSMOS_DATABASE!);
      eventStore = new CosmosEventStore(
        database as any
      );
      
      // Initialize the Cosmos DB container
      try {
        const result = await eventStore.initialize();
        if (result.isErr()) {
          logger.error('Failed to initialize Cosmos DB container:', result.error);
          throw result.error;
        }
      } catch (error) {
        logger.error('Failed to initialize Cosmos DB:', error);
        throw error;
      }
      break;
    }
    
    default: {
      throw new Error(`Storage type '${config.STORAGE_TYPE}' not supported. Use 'postgres' or 'cosmos'.`);
    }
  }

  // Create DaprClient for actor proxy factory
  const daprClient = new DaprClient({
    daprHost: "127.0.0.1",
    daprPort: String(config.DAPR_HTTP_PORT)
  });

  // Create actor proxy factory
  const actorProxyFactory: ActorProxyFactory = {
    createActorProxy: (actorId, actorType: string) => {
      // Type-safe actor ID extraction
      let actorIdStr: string;
      if (typeof actorId === 'string') {
        actorIdStr = actorId;
      } else if (actorId && typeof actorId === 'object' && 'getId' in actorId && typeof (actorId as { getId: () => string }).getId === 'function') {
        actorIdStr = (actorId as { getId: () => string }).getId();
      } else {
        actorIdStr = String(actorId);
      }
      // Only log actor proxy creation for non-MultiProjectorActor types
      if (actorType !== 'MultiProjectorActor') {
        logger.debug(`Creating actor proxy for ${actorType}/${actorIdStr}`);
      }
      
      // For now, we only need MultiProjectorActor proxies in this service
      if (actorType === 'MultiProjectorActor') {
        // Create actor class using factory
        const factory = MultiProjectorActorFactory as MultiProjectorActorFactoryCreateMethod;
        const MultiProjectorActorClass = factory.createActorClass();
        const builder = new ActorProxyBuilder(MultiProjectorActorClass, daprClient);
        return builder.build(new ActorId(actorIdStr));
      }
      
      // Fallback for other actor types
      console.warn(`[ActorProxyFactory] Unknown actor type: ${actorType}`);
      return null;
    }
  };

  // Create a simple serialization service
  const serializationService: SerializationService = {
    async deserializeAggregateAsync(surrogate: unknown) {
      return surrogate;
    },
    async serializeAggregateAsync(aggregate: unknown) {
      return aggregate;
    }
  };

  // Configure MultiProjectorActorFactory
  const configureMethod = MultiProjectorActorFactory as MultiProjectorActorFactoryConfigureMethod;
  configureMethod.configure(
    domainTypes,
    {}, // service provider
    actorProxyFactory,
    serializationService,
    eventStore
  );

  // Create actor class with proper typing
  const MultiProjectorActorClass = MultiProjectorActor;
  

  // Create DaprServer
  const daprServer = new DaprServer({
    serverHost: "127.0.0.1",
    serverPort: String(config.PORT),
    serverHttp: app, // Pass the configured Express app
    communicationProtocol: CommunicationProtocolEnum.HTTP,
    clientOptions: {
      daprHost: "127.0.0.1",
      daprPort: String(config.DAPR_HTTP_PORT),
      communicationProtocol: CommunicationProtocolEnum.HTTP
    }
  });
  
  // Register MultiProjectorActor
  await daprServer.actor.registerActor(MultiProjectorActorClass);
  
  // Initialize actor runtime
  await daprServer.actor.init();

  
  return daprServer;
}

// Start the server
startServer().catch((error) => {
  console.error('Failed to start server:', error);
  process.exit(1);
});