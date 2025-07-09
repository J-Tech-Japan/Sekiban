import express from 'express';
import cors from 'cors';
import helmet from 'helmet';
import compression from 'compression';
import morgan from 'morgan';
import { config } from './config/index.js';
import { createExecutor, cleanup } from './setup/executor-debug.js';
import { errorHandler } from './middleware/error-handler.js';
import { healthRoutes } from './routes/health-routes.js';
import { taskRoutes } from './routes/task-routes-debug.js';
import { eventRoutes } from './routes/event-routes.js';
import { DaprServer, DaprClient, CommunicationProtocolEnum, HttpMethod, ActorProxyBuilder, ActorId } from '@dapr/dapr';
import { 
  AggregateActorFactory, 
  AggregateEventHandlerActorFactory
} from '@sekiban/dapr';
import { InMemoryEventStore, StorageProviderType } from '@sekiban/core';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import logger from './utils/logger.js';


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
    console.log(`[${new Date().toISOString()}] ${req.method} ${req.path}`);
    logger.debug(`[${new Date().toISOString()}] ${req.method} ${req.path}`);
    if (req.path.startsWith('/actors/')) {
      console.log(`[ACTOR] Actor route called: ${req.method} ${req.path}`);
      logger.info(`Actor route called: ${req.method} ${req.path}`);
    }
    next();
  });

  // CRITICAL: Convert POST to PUT for actor method calls
  // This middleware MUST come before DaprServer setup
  app.use((req, res, next) => {
    if (req.path.includes('/method/') && req.method === 'POST') {
      req.method = 'PUT';
      console.log(`[ACTOR] Converted POST to PUT for actor method: ${req.path}`);
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
    console.log('[EXECUTOR] Initializing executor...');
    await createExecutor();
    console.log('[EXECUTOR] Executor initialized successfully');
  } catch (error) {
    console.error('[EXECUTOR] Failed to initialize executor:', error);
    process.exit(1);
  }

  // Create DaprServer and pass the Express app
  console.log('[DAPR] Setting up Dapr actors...');
  const daprServer = await setupDaprActorsWithApp(app);

  // Start the DaprServer
  console.log('[DAPR] Starting DaprServer...');
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
  console.log('[DAPR SETUP] Starting Dapr actor setup...');

  // Initialize domain types
  console.log('[DAPR SETUP] Creating domain types...');
  const domainTypes = createTaskDomainTypes();

  // Initialize event store (using in-memory for development)
  console.log('[DAPR SETUP] Creating in-memory event store...');
  const eventStore = new InMemoryEventStore({
    type: StorageProviderType.InMemory,
    enableLogging: config.NODE_ENV === 'development'
  });

  // Create DaprClient for actor proxy factory
  console.log('[DAPR SETUP] Creating DaprClient...');
  const daprClient = new DaprClient({
    daprHost: "127.0.0.1",
    daprPort: String(config.DAPR_HTTP_PORT),
    communicationProtocol: CommunicationProtocolEnum.HTTP
  });

  // Create actor proxy factory using ActorProxyBuilder
  console.log('[DAPR SETUP] Creating actor proxy factory with ActorProxyBuilder...');
  const actorProxyFactory = {
    createActorProxy: (actorId: any, actorType: string) => {
      console.log(`[ACTOR PROXY] Creating actor proxy for ${actorType}/${actorId.id}`);
      logger.debug(`Creating actor proxy for ${actorType}/${actorId.id}`);
      
      // Define the interface for the actor
      interface AggregateActorInterface {
        executeCommandAsync(commandAndMetadata: any): Promise<string>;
        queryAsync(query: any): Promise<any>;
        loadAggregateAsync(partitionKeys: any): Promise<any>;
      }
      
      // Create ActorProxyBuilder with the actual actor class
      const ActorClass = AggregateActorFactory.createActorClass();
      const builder = new ActorProxyBuilder<AggregateActorInterface>(ActorClass, daprClient);
      
      // Create actor proxy
      const actorIdObj = new ActorId(actorId.id || actorId);
      const proxy = builder.build(actorIdObj);
      
      console.log(`[ACTOR PROXY] Created proxy for actor ${actorIdObj.getId()}`);
      
      // Return a wrapper that matches the expected interface
      return {
        invoke: async (methodName: string, data: any) => {
          console.log(`[ACTOR PROXY] Invoking ${methodName} on actor ${actorIdObj.getId()}`);
          const startTime = Date.now();
          
          try {
            let result: any;
            switch (methodName) {
              case 'executeCommandAsync':
                result = await proxy.executeCommandAsync(data);
                break;
              case 'queryAsync':
                result = await proxy.queryAsync(data);
                break;
              case 'loadAggregateAsync':
                result = await proxy.loadAggregateAsync(data);
                break;
              default:
                throw new Error(`Unknown method: ${methodName}`);
            }
            
            const duration = Date.now() - startTime;
            console.log(`[ACTOR PROXY] ${methodName} completed in ${duration}ms`);
            return result;
          } catch (error) {
            const duration = Date.now() - startTime;
            console.error(`[ACTOR PROXY] ${methodName} failed after ${duration}ms:`, error);
            throw error;
          }
        }
      };
    }
  };

  // Create a simple serialization service
  console.log('[DAPR SETUP] Creating serialization service...');
  const serializationService = {
    async deserializeAggregateAsync(surrogate: any) {
      console.log('[SERIALIZATION] Deserializing aggregate...');
      return surrogate;
    },
    async serializeAggregateAsync(aggregate: any) {
      console.log('[SERIALIZATION] Serializing aggregate...');
      return aggregate;
    }
  };

  // Configure actor factories
  console.log('[DAPR SETUP] Configuring AggregateActorFactory...');
  AggregateActorFactory.configure(
    domainTypes,
    {}, // service provider
    actorProxyFactory,
    serializationService,
    eventStore
  );

  console.log('[DAPR SETUP] Configuring AggregateEventHandlerActorFactory...');
  AggregateEventHandlerActorFactory.configure(eventStore);

  // Create DaprServer and pass the Express app directly
  console.log('[DAPR SETUP] Creating DaprServer...');
  const daprServer = new DaprServer({
    serverHost: "127.0.0.1",
    serverPort: String(config.PORT),
    serverHttp: app, // Pass the configured Express app
    communicationProtocol: CommunicationProtocolEnum.HTTP,
    clientOptions: {
      daprHost: "127.0.0.1",
      daprPort: String(config.DAPR_HTTP_PORT),
      communicationProtocol: CommunicationProtocolEnum.HTTP,
      actor: {
        actorIdleTimeout: "1h",
        actorScanInterval: "30s",
        drainOngoingCallTimeout: "1m",
        drainRebalancedActors: true
      }
    }
  });

  // CRITICAL: Initialize actor runtime FIRST (before registering actors)
  console.log('[DAPR SETUP] Initializing actor runtime...');
  await daprServer.actor.init();
  logger.info('Actor runtime initialized');
  console.log('[DAPR SETUP] Actor runtime initialized successfully');

  // Register actors AFTER init
  console.log('[DAPR SETUP] Creating AggregateActor class...');
  const ActorClass = AggregateActorFactory.createActorClass();
  console.log('[DAPR SETUP] Actor class created:', ActorClass.name);
  
  console.log('[DAPR SETUP] Registering AggregateActor...');
  daprServer.actor.registerActor(ActorClass);
  logger.info('Registered AggregateActor');
  console.log('[DAPR SETUP] AggregateActor registered successfully');

  console.log('[DAPR SETUP] Registering AggregateEventHandlerActor...');
  daprServer.actor.registerActor(AggregateEventHandlerActorFactory.createActorClass());
  logger.info('Registered AggregateEventHandlerActor');
  console.log('[DAPR SETUP] AggregateEventHandlerActor registered successfully');
  
  logger.info('Dapr actors integrated with Express app');
  console.log('[DAPR SETUP] Dapr actor setup completed');
  
  return daprServer;
}

// Start the server
startServer().catch((error) => {
  console.error('Failed to start server:', error);
  process.exit(1);
});