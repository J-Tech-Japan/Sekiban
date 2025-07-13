import express from 'express';
import cors from 'cors';
import helmet from 'helmet';
import compression from 'compression';
import morgan from 'morgan';
import pg from 'pg';
<<<<<<< HEAD
import { DaprServer, DaprClient, CommunicationProtocolEnum, HttpMethod, ActorProxyBuilder, ActorId } from '@dapr/dapr';
import { MultiProjectorActorFactory, getDaprCradle } from '@sekiban/dapr';
import { InMemoryEventStore, StorageProviderType } from '@sekiban/core';
import { PostgresEventStore } from '@sekiban/postgres';
import { createTaskDomainTypes } from '@dapr-sample/domain';

const { Pool } = pg;

const config = {
  NODE_ENV: process.env.NODE_ENV || 'development',
  PORT: parseInt(process.env.PORT || '3003', 10),
  DAPR_HTTP_PORT: parseInt(process.env.DAPR_HTTP_PORT || '3503', 10),
  DAPR_APP_ID: process.env.DAPR_APP_ID || 'dapr-sample-api-multi-projector',
  API_PREFIX: process.env.API_PREFIX || '/api/v1',
  CORS_ORIGIN: process.env.CORS_ORIGIN || '*',
  USE_POSTGRES: process.env.USE_POSTGRES === 'true',
  DATABASE_URL: process.env.DATABASE_URL || 'postgresql://sekiban:sekiban_password@localhost:5432/sekiban_events'
};

=======
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

>>>>>>> origin/main
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
<<<<<<< HEAD
    console.log(`[${new Date().toISOString()}] ${req.method} ${req.path}`);
    if (req.path.startsWith('/actors/')) {
      console.log(`Actor route called: ${req.method} ${req.path}`);
=======
    logger.debug(`[${new Date().toISOString()}] ${req.method} ${req.path}`);
    if (req.path.startsWith('/actors/')) {
      logger.info(`Actor route called: ${req.method} ${req.path}`);
>>>>>>> origin/main
    }
    next();
  });

  // CRITICAL: Convert POST to PUT for actor method calls
  app.use((req, res, next) => {
    if (req.path.includes('/method/') && req.method === 'POST') {
      req.method = 'PUT';
<<<<<<< HEAD
      console.log(`Converted POST to PUT for actor method: ${req.path}`);
=======
      logger.debug(`Converted POST to PUT for actor method: ${req.path}`);
>>>>>>> origin/main
    }
    next();
  });
  
<<<<<<< HEAD
  // Health routes
  app.get('/health', (_req, res) => {
    res.json({ 
      status: 'healthy',
      service: 'api-multi-projector',
      timestamp: new Date().toISOString() 
    });
  });

  app.get('/ready', (_req, res) => {
    res.json({ 
      status: 'ready',
      service: 'api-multi-projector',
      timestamp: new Date().toISOString() 
    });
  });

  // Dapr pub/sub subscription endpoint
  app.get('/dapr/subscribe', (_req, res) => {
    console.log('[PubSub] Subscription endpoint called');
    res.json([
      {
        pubsubname: 'pubsub',
        topic: 'sekiban-events',
        route: '/events',
        metadata: {
          rawPayload: 'false'
        }
      }
    ]);
  });

  // Event handler endpoint
  app.post('/events', async (req, res) => {
    console.log('[PubSub] Received event:', {
      topic: req.headers['ce-topic'],
      type: req.headers['ce-type'],
      id: req.headers['ce-id'],
      source: req.headers['ce-source']
    });
    console.log('[PubSub] Request body:', JSON.stringify(req.body, null, 2));
    
    try {
      // Dapr wraps the event in a cloud event envelope
      const eventData = req.body.data || req.body;
      
      // Distribute event to all relevant MultiProjectorActors
      await distributeEventToProjectors(eventData);
      
      // Return 200 OK to acknowledge message
      res.status(200).json({ success: true });
    } catch (error) {
      console.error('[PubSub] Error processing event:', error);
      // Return 500 to retry later
      res.status(500).json({ 
        error: error instanceof Error ? error.message : 'Unknown error' 
      });
    }
  });

  // Error handling
  app.use((err: any, req: express.Request, res: express.Response, _next: express.NextFunction) => {
    console.error(`Error handling request ${req.method} ${req.path}:`, err.message);
    const status = err.status || 500;
    const message = err.message || 'Internal server error';
    res.status(status).json({
      error: {
        message,
        status,
        timestamp: new Date().toISOString(),
      },
    });
  });
=======
  // Routes
  app.use('/', healthRoutes);
  app.use(config.API_PREFIX, multiProjectorRoutes);

  // Error handling (must be last)
  app.use(errorHandler);
>>>>>>> origin/main

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

<<<<<<< HEAD
/**
 * Distribute event to all relevant MultiProjectorActors
 */
async function distributeEventToProjectors(eventData: any) {
  console.log('[Distributor] Processing event:', {
    type: eventData?.type,
    aggregateType: eventData?.aggregateType,
    aggregateId: eventData?.aggregateId
  });

  // Use hardcoded list for now
  const multiProjectorTypes = [
    { name: 'TaskProjector' },
    { name: 'UserProjector' },
    { name: 'WeatherForecastProjector' }
  ];

  console.log(`[Distributor] Using ${multiProjectorTypes.length} multi-projector types:`, multiProjectorTypes.map(p => p.name));

  // Create DaprClient for invoking actors
  const daprClient = new DaprClient({
    daprHost: "127.0.0.1",
    daprPort: String(config.DAPR_HTTP_PORT)
  });

  // Send event to each MultiProjectorActor
  const promises = multiProjectorTypes.map(async (projectorType) => {
    const actorId = `aggregatelistprojector-${projectorType.name.toLowerCase()}`;
    
    try {
      console.log(`[Distributor] Sending event to actor: ${actorId}`);
      
      // Invoke the actor's receiveEvent method
      await daprClient.invoker.invoke(
        config.DAPR_APP_ID,
        `actors/MultiProjectorActor/${actorId}/method/receiveEventAsync`,
        HttpMethod.PUT,
        [eventData]
      );
      
      console.log(`[Distributor] Successfully sent event to actor: ${actorId}`);
    } catch (error) {
      console.error(`[Distributor] Failed to send event to actor ${actorId}:`, error);
      // Don't fail the whole operation if one actor fails
    }
  });

  // Wait for all distributions to complete
  await Promise.allSettled(promises);
  
  console.log('[Distributor] Event distribution completed');
}

async function setupDaprActorsWithApp(app: express.Express) {
  console.log('Setting up Dapr actors with Express app...');
=======
async function setupDaprActorsWithApp(app: express.Express) {
  logger.info('Setting up Dapr actors with Express app...');
>>>>>>> origin/main

  // Initialize domain types
  const domainTypes = createTaskDomainTypes();

  // Choose storage type based on environment variable or config
  const usePostgres = config.USE_POSTGRES;
  
  let eventStore: any;
  
  if (usePostgres) {
    // Initialize PostgreSQL event store
<<<<<<< HEAD
    console.log('Using PostgreSQL event store');
=======
    logger.info('Using PostgreSQL event store');
>>>>>>> origin/main
    const pool = new Pool({
      connectionString: config.DATABASE_URL
    });
    
    eventStore = new PostgresEventStore(pool);
    
    // Initialize the database schema
<<<<<<< HEAD
    console.log('Initializing PostgreSQL schema...');
    try {
      const result = await eventStore.initialize();
      if (result.isOk()) {
        console.log('PostgreSQL schema initialized successfully');
      } else {
        console.error('Failed to initialize PostgreSQL schema:', result.error);
        throw result.error;
      }
    } catch (error) {
      console.error('Failed to initialize PostgreSQL:', error);
=======
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
>>>>>>> origin/main
      throw error;
    }
  } else {
    // Initialize in-memory event store
<<<<<<< HEAD
    console.log('Using in-memory event store');
=======
    logger.info('Using in-memory event store');
>>>>>>> origin/main
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
<<<<<<< HEAD
      console.log(`Creating actor proxy for ${actorType}/${actorId.id}`);
=======
      logger.debug(`Creating actor proxy for ${actorType}/${actorId.id}`);
>>>>>>> origin/main
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
  
<<<<<<< HEAD
  console.log('[DEBUG] MultiProjectorActorClass name:', MultiProjectorActorClass.name);
=======
  logger.info('[DEBUG] MultiProjectorActorClass name:', MultiProjectorActorClass.name);
>>>>>>> origin/main

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
<<<<<<< HEAD
  console.log('Registered MultiProjectorActor');
  
  // Initialize actor runtime
  console.log('Initializing actor runtime...');
  await daprServer.actor.init();
  console.log('Actor runtime initialized');

  console.log('Dapr actors integrated with Express app');
=======
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
>>>>>>> origin/main
  
  return daprServer;
}

// Start the server
startServer().catch((error) => {
  console.error('Failed to start server:', error);
  process.exit(1);
});