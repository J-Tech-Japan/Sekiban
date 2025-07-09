import express from 'express';
import cors from 'cors';
import helmet from 'helmet';
import compression from 'compression';
import morgan from 'morgan';
import { config } from './config/index.js';
import { createExecutor, cleanup } from './setup/executor.js';
import { errorHandler } from './middleware/error-handler.js';
import { healthRoutes } from './routes/health-routes.js';
import { taskRoutes } from './routes/task-routes.js';
import { eventRoutes } from './routes/event-routes.js';
import { DaprServer, CommunicationProtocolEnum } from '@dapr/dapr';
import { 
  AggregateActorFactory, 
  AggregateEventHandlerActorFactory 
} from '@sekiban/dapr';
import { InMemoryEventStore, StorageProviderType } from '@sekiban/core';
import { createTaskDomainTypes } from '@dapr-sample/domain';
import logger from './utils/logger.js';

async function setupDaprActors(app: express.Express) {
  logger.info('Setting up Dapr actors...');

  // Initialize domain types
  const domainTypes = createTaskDomainTypes();

  // Initialize event store (using in-memory for development)
  const eventStore = new InMemoryEventStore({
    type: StorageProviderType.InMemory,
    enableLogging: config.NODE_ENV === 'development'
  });

  // Create a simple actor proxy factory
  const actorProxyFactory = {
    createActorProxy: (actorId: any, actorType: string) => {
      logger.debug(`Creating actor proxy for ${actorType}/${actorId.id}`);
      return {} as any;
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
    serializationService
  );

  AggregateEventHandlerActorFactory.configure(eventStore);

  // Create DaprServer with our Express instance
  const daprServer = new DaprServer({
    serverHost: "127.0.0.1",
    serverPort: String(config.PORT),
    serverHttp: app, // Pass our Express app here
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

  // Initialize actor runtime
  await daprServer.actor.init();
  logger.info('Actor runtime initialized');

  // Register actors
  daprServer.actor.registerActor(AggregateActorFactory.createActorClass());
  logger.info('Registered AggregateActor');

  daprServer.actor.registerActor(AggregateEventHandlerActorFactory.createActorClass());
  logger.info('Registered AggregateEventHandlerActor');
  
  logger.info('Dapr actors integrated with Express app');
  
  return daprServer;
}

async function startServer() {
  const app = express();

  // Middleware
  app.use(helmet());
  app.use(cors({ origin: config.CORS_ORIGIN }));
  app.use(compression());
  app.use(morgan(config.NODE_ENV === 'production' ? 'combined' : 'dev'));
  app.use(express.json());
  app.use(express.urlencoded({ extended: true }));

  // Setup Dapr actors BEFORE other routes
  const daprServer = await setupDaprActors(app);
  
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

  // Start the DaprServer (which includes our Express app)
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

// Start the server
startServer().catch((error) => {
  console.error('Failed to start server:', error);
  process.exit(1);
});