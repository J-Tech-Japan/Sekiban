import express from 'express';
import cors from 'cors';
import helmet from 'helmet';
import compression from 'compression';
import morgan from 'morgan';
import pg from 'pg';
import { config } from './config/index.js';
import { createExecutor, cleanup } from './setup/executor.js';
import { errorHandler } from './middleware/error-handler.js';
import { healthRoutes } from './routes/health-routes.js';
import { taskRoutes } from './routes/task-routes.js';
import { eventRoutes } from './routes/event-routes.js';
import { DaprServer, DaprClient, CommunicationProtocolEnum, HttpMethod, ActorProxyBuilder, ActorId } from '@dapr/dapr';
import { 
  AggregateActorFactory, 
  AggregateEventHandlerActorFactory
} from '@sekiban/dapr';
import { InMemoryEventStore, StorageProviderType } from '@sekiban/core';
import { PostgresEventStore } from '@sekiban/postgres';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import logger from './utils/logger.js';

const { Pool } = pg;


async function startServer() {
  const app = express();

  // Middleware BEFORE DaprServer setup
  app.use(helmet());
  app.use(cors({ origin: config.CORS_ORIGIN }));
  app.use(compression());
  app.use(morgan(config.NODE_ENV === 'production' ? 'combined' : 'dev'));
  app.use(express.json());
  app.use(express.urlencoded({ extended: true }));

  // Debug middleware to log all requests
  app.use((req, res, next) => {
    logger.debug(`[${new Date().toISOString()}] ${req.method} ${req.path}`);
    if (req.path.startsWith('/actors/')) {
      logger.info(`Actor route called: ${req.method} ${req.path}`);
    }
    next();
  });

  // CRITICAL: Convert POST to PUT for actor method calls
  // This middleware MUST come before DaprServer setup
  app.use((req, res, next) => {
    if (req.path.includes('/method/') && req.method === 'POST') {
      req.method = 'PUT';
      logger.debug(`Converted POST to PUT for actor method: ${req.path}`);
    }
    next();
  });
  
  // Routes
  app.use('/', healthRoutes);
  app.use('/', eventRoutes);
  app.use(config.API_PREFIX, taskRoutes);

  // Error handling (must be last)
  app.use(errorHandler);

  // Initialize executor
  try {
    await createExecutor();
    console.log('Executor initialized successfully');
  } catch (error) {
    console.error('Failed to initialize executor:', error);
    process.exit(1);
  }

  // Create DaprServer and pass the Express app
  const daprServer = await setupDaprActorsWithApp(app);

  // Start the DaprServer
  await daprServer.start();
  
  console.log(`
🚀 Server is running!
📡 Environment: ${config.NODE_ENV}
🔗 URL: http://localhost:${config.PORT}
🔗 API: http://localhost:${config.PORT}${config.API_PREFIX}
🎭 Dapr App ID: ${config.DAPR_APP_ID}
🎭 Actors: AggregateActor, AggregateEventHandlerActor
  `);

  // Graceful shutdown
  const gracefulShutdown = async (signal: string) => {
    console.log(`\n${signal} received, starting graceful shutdown...`);
    
    // Force exit after 30 seconds
    const forceExitTimeout = setTimeout(() => {
      console.error('Forced shutdown after timeout');
      process.exit(1);
    }, 30000);
    
    try {
      // Stop DaprServer
      await daprServer.stop();
      console.log('DaprServer stopped');
      
      // Cleanup database connections
      await cleanup();
      console.log('Cleanup completed');
      
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
  logger.info('Setting up Dapr actors with Express app...');

  // Initialize domain types
  const domainTypes = createTaskDomainTypes();

  // Choose storage type based on environment variable or config
  const usePostgres = config.USE_POSTGRES;
  
  let eventStore: any;
  
  if (usePostgres) {
    // Initialize PostgreSQL event store
    logger.info('Using PostgreSQL event store');
    const pool = new Pool({
      connectionString: config.DATABASE_URL
    });
    
    eventStore = new PostgresEventStore(pool);
    
    // Initialize the database schema
    logger.info('Initializing PostgreSQL schema...');
    try {
      const result = await eventStore.initialize();
      if (result.isOk()) {
        logger.info('PostgreSQL schema initialized successfully');
      } else {
        logger.error('Failed to initialize PostgreSQL schema:', result.error);
        throw result.error;
      }
    } catch (error) {
      logger.error('Failed to initialize PostgreSQL:', error);
      throw error;
    }
  } else {
    // Initialize in-memory event store (commented out for easy switching)
    logger.info('Using in-memory event store');
    eventStore = new InMemoryEventStore({
      type: StorageProviderType.InMemory,
      enableLogging: config.NODE_ENV === 'development'
    });
  }

  // Create DaprClient for actor proxy factory
  const daprClient = new DaprClient({
    daprHost: "127.0.0.1",
    daprPort: String(config.DAPR_HTTP_PORT)
  });

  // Create actor proxy factory that uses DaprClient with ActorProxyBuilder
  const actorProxyFactory = {
    createActorProxy: (actorId: any, actorType: string) => {
      logger.debug(`Creating actor proxy for ${actorType}/${actorId.id}`);
      const actorIdStr = actorId.id || actorId;
      
      // Use ActorProxyBuilder for proper actor-to-actor communication
      if (actorType === 'AggregateEventHandlerActor') {
        console.log(`[ActorProxyFactory] Creating EventHandlerActor proxy using ActorProxyBuilder for ${actorIdStr}`);
        const EventHandlerActorClass = AggregateEventHandlerActorFactory.createActorClass();
        const builder = new ActorProxyBuilder(EventHandlerActorClass, daprClient);
        return builder.build(new ActorId(actorIdStr));
      } else if (actorType === 'AggregateActor') {
        console.log(`[ActorProxyFactory] Creating AggregateActor proxy using ActorProxyBuilder for ${actorIdStr}`);
        const AggregateActorClass = AggregateActorFactory.createActorClass();
        const builder = new ActorProxyBuilder(AggregateActorClass, daprClient);
        return builder.build(new ActorId(actorIdStr));
      } else {
        // Fallback for unknown actor types
        console.warn(`[ActorProxyFactory] Unknown actor type: ${actorType}, using direct HTTP calls`);
        return {
          executeCommandAsync: async (data: any) => {
            console.log(`[ActorProxy] Calling executeCommandAsync on ${actorType}/${actorIdStr}`);
            return daprClient.invoker.invoke(
              config.DAPR_APP_ID,
              `actors/${actorType}/${actorIdStr}/method/executeCommandAsync`,
              HttpMethod.PUT, 
              data
            );
          },
          queryAsync: async (data: any) => {
            return daprClient.invoker.invoke(
              config.DAPR_APP_ID,
              `actors/${actorType}/${actorIdStr}/method/queryAsync`,
              HttpMethod.PUT, 
              data
            );
          },
          loadAggregateAsync: async (data: any) => {
            return daprClient.invoker.invoke(
              config.DAPR_APP_ID,
              `actors/${actorType}/${actorIdStr}/method/loadAggregateAsync`,
              HttpMethod.PUT, 
              data
            );
          },
          appendEventsAsync: async (expectedLastSortableUniqueId: string, events: any[]) => {
            console.log(`[ActorProxy] Calling appendEventsAsync on ${actorType}/${actorIdStr}`);
            return daprClient.invoker.invoke(
              config.DAPR_APP_ID,
              `actors/${actorType}/${actorIdStr}/method/appendEventsAsync`,
              HttpMethod.PUT, 
              [expectedLastSortableUniqueId, events] // Pass as array for proper parameter passing
            );
          }
        };
      }
    }
  };

  // Create a simple serialization service
  const serializationService = {
    async deserializeAggregateAsync(surrogate: any) {
      return surrogate;
    },
    async serializeAggregateAsync(aggregate: any) {
      return aggregate;
    }
  };

  // Configure actor factories
  AggregateActorFactory.configure(
    domainTypes,
    {}, // service provider
    actorProxyFactory,
    serializationService,
    eventStore
  );

  AggregateEventHandlerActorFactory.configure(eventStore);

  // Create actor classes before DaprServer initialization
  const AggregateActorClass = AggregateActorFactory.createActorClass();
  const EventHandlerActorClass = AggregateEventHandlerActorFactory.createActorClass();
  
  console.log('[DEBUG] AggregateActorClass name:', AggregateActorClass.name);
  console.log('[DEBUG] EventHandlerActorClass name:', EventHandlerActorClass.name);

  // Create DaprServer and pass the Express app directly
  const daprServer = new DaprServer({
    serverHost: "127.0.0.1",
    serverPort: String(config.PORT),
    serverHttp: app, // Pass the configured Express app
    communicationProtocol: CommunicationProtocolEnum.HTTP,
    clientOptions: {
      daprHost: "127.0.0.1",
      daprPort: String(config.DAPR_HTTP_PORT),
      communicationProtocol: CommunicationProtocolEnum.HTTP
    },
    // Register all actor types during initialization
    actor: {
      actorIdleTimeout: "1h",
      actorScanInterval: "30s",
      drainOngoingCallTimeout: "1m",
      drainRebalancedActors: true,
      actorTypes: [
        AggregateActorClass,
        EventHandlerActorClass
      ]
    }
  });

  // CRITICAL: Initialize actor runtime FIRST (actors are already registered in constructor)
  await daprServer.actor.init();
  logger.info('Actor runtime initialized');
  logger.info('Registered actors: AggregateActor, AggregateEventHandlerActor');

  // Add diagnostic logging to catch any exceptions
  console.log('[DEBUG] Adding diagnostic logging...');
  
  // Override the actor method handler to add logging
  const originalActorHandler = (daprServer as any).actor?.actorHandler;
  if (originalActorHandler) {
    (daprServer as any).actor.actorHandler = async (req: any, res: any) => {
      console.log('[DIAGNOSTIC] Actor handler called for:', req.url);
      try {
        await originalActorHandler.call((daprServer as any).actor, req, res);
      } catch (e) {
        console.error('[DIAGNOSTIC] Actor handler error:', e);
        console.error('[DIAGNOSTIC] Stack trace:', (e as Error).stack);
        throw e;
      }
    };
    console.log('[DEBUG] Diagnostic handler installed');
  } else {
    console.log('[DEBUG] Could not install diagnostic handler');
  }

  
  logger.info('Dapr actors integrated with Express app');
  
  return daprServer;
}

// Start the server
startServer().catch((error) => {
  console.error('Failed to start server:', error);
  process.exit(1);
});