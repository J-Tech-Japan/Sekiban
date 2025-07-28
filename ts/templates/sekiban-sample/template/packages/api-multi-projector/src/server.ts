import express from 'express';
import cors from 'cors';
import helmet from 'helmet';
import compression from 'compression';
import morgan from 'morgan';
import { Pool } from 'pg';
import { DaprServer, DaprClient, CommunicationProtocolEnum, HttpMethod, ActorProxyBuilder, ActorId } from '@dapr/dapr';
import { MultiProjectorActorFactory, getDaprCradle, MultiProjectorActor } from '@sekiban/dapr';
import { InMemoryEventStore, StorageProviderType, IEventStore } from '@sekiban/core';
import { PostgresEventStore } from '@sekiban/postgres';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import { config } from './config/index.js';
import { errorHandler } from './middleware/error-handler.js';
import { healthRoutes } from './routes/health-routes.js';
import { multiProjectorRoutes } from './routes/multi-projector-routes.js';
import logger from './utils/logger.js';
import type { MultiProjectorActorFactoryConfigureMethod, MultiProjectorActorFactoryCreateMethod } from './types/factory-types.js';
import type { PubSubEventData, SerializationService, ActorProxyFactory } from './types/domain-types.js';

async function startServer() {
  const app = express();

  // Middleware
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
  app.use((req, res, next) => {
    if (req.path.includes('/method/') && req.method === 'POST') {
      req.method = 'PUT';
      logger.debug(`Converted POST to PUT for actor method: ${req.path}`);
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
  logger.info('Setting up Dapr actors with Express app...');

  // Initialize domain types
  const domainTypes = createTaskDomainTypes();

  // Choose storage type based on environment variable or config
  const usePostgres = config.USE_POSTGRES;
  
  let eventStore: any; // Use any to avoid neverthrow version mismatch
  
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
    // Initialize in-memory event store
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

  // Create actor proxy factory
  const actorProxyFactory: ActorProxyFactory = {
    createActorProxy: (actorId, actorType: string) => {
      const actorIdObj = typeof actorId === 'string' ? { id: actorId } : actorId;
      const actorIdStr = 'id' in actorIdObj ? (actorIdObj as { id: string }).id : (actorIdObj as any).getId ? (actorIdObj as any).getId() : String(actorId);
      logger.debug(`Creating actor proxy for ${actorType}/${actorIdStr}`);
      
      // For now, we only need MultiProjectorActor proxies in this service
      if (actorType === 'MultiProjectorActor') {
        // Type assertion needed due to factory pattern limitations
        const factory = MultiProjectorActorFactory as unknown as MultiProjectorActorFactoryCreateMethod;
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
  const configureMethod = MultiProjectorActorFactory as unknown as MultiProjectorActorFactoryConfigureMethod;
  configureMethod.configure(
    domainTypes,
    {}, // service provider
    actorProxyFactory,
    serializationService,
    eventStore
  );

  // Create actor class - use direct reference to avoid type issues
  const MultiProjectorActorClass = MultiProjectorActor as any;
  
  logger.info('[DEBUG] MultiProjectorActorClass name:', MultiProjectorActorClass.name);

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
  logger.info('Registered MultiProjectorActor');
  
  // Initialize actor runtime
  logger.info('Initializing actor runtime...');
  await daprServer.actor.init();
  logger.info('Actor runtime initialized');

  // Add diagnostic logging
  console.log('[DEBUG] Adding diagnostic logging...');
  
  // Type assertion for private properties
  // Type for Express Request and Response
  interface ExpressRequest {
    url: string;
    [key: string]: unknown;
  }
  
  interface ExpressResponse {
    [key: string]: unknown;
  }
  
  const serverWithActorHandler = daprServer as unknown as {
    actor?: {
      actorHandler?: (req: ExpressRequest, res: ExpressResponse) => Promise<void>;
    };
  };
  
  const originalActorHandler = serverWithActorHandler.actor?.actorHandler;
  if (originalActorHandler && serverWithActorHandler.actor) {
    serverWithActorHandler.actor.actorHandler = async (req: ExpressRequest, res: ExpressResponse) => {
      console.log('[DIAGNOSTIC] Actor handler called for:', req.url);
      try {
        await originalActorHandler.call(serverWithActorHandler.actor, req, res);
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