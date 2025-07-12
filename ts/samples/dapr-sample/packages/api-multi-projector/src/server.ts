import express from 'express';
import cors from 'cors';
import helmet from 'helmet';
import compression from 'compression';
import morgan from 'morgan';
import pg from 'pg';
import { config } from './config/index.js';
import { errorHandler } from './middleware/error-handler.js';
import { healthRoutes } from './routes/health-routes.js';
import { multiProjectorRoutes } from './routes/multi-projector-routes.js';
import { DaprServer, DaprClient, CommunicationProtocolEnum, HttpMethod, ActorProxyBuilder, ActorId } from '@dapr/dapr';
import { MultiProjectorActorFactory } from '@sekiban/dapr';
import { InMemoryEventStore, StorageProviderType } from '@sekiban/core';
import { PostgresEventStore } from '@sekiban/postgres';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import logger from './utils/logger.js';

const { Pool } = pg;

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
  const actorProxyFactory = {
    createActorProxy: (actorId: any, actorType: string) => {
      logger.debug(`Creating actor proxy for ${actorType}/${actorId.id}`);
      const actorIdStr = actorId.id || actorId;
      
      // For now, we only need MultiProjectorActor proxies in this service
      if (actorType === 'MultiProjectorActor') {
        const MultiProjectorActorClass = MultiProjectorActorFactory.createActorClass();
        const builder = new ActorProxyBuilder(MultiProjectorActorClass, daprClient);
        return builder.build(new ActorId(actorIdStr));
      }
      
      // Fallback for other actor types
      console.warn(`[ActorProxyFactory] Unknown actor type: ${actorType}`);
      return null;
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

  // Configure MultiProjectorActorFactory
  MultiProjectorActorFactory.configure(
    domainTypes,
    {}, // service provider
    actorProxyFactory,
    serializationService,
    eventStore
  );

  // Create actor class
  const MultiProjectorActorClass = MultiProjectorActorFactory.createActorClass();
  
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