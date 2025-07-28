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
  AggregateEventHandlerActorFactory,
  type EventHandlingResponse
} from '@sekiban/dapr';
import { StorageProviderType } from '@sekiban/core';
// CLAUDE.md: InMemoryEventStore removed - only proper storage implementations allowed
// Dynamic imports for storage providers to avoid module resolution errors
import { createCosmosEventStore } from '@sekiban/cosmos';
// Import domain types at the top to ensure registration happens
import '@dapr-sample/domain';
import { createTaskDomainTypes, AggregateEventHandlerActorBase } from '@dapr-sample/domain';
import { globalRegistry } from '@sekiban/core';
import logger from './utils/logger.js';

// Global registry is initialized after domain import

const { Pool } = pg;


async function startServer() {
  const app = express();

  // Initialize domain types FIRST - before any actor setup
  const domainTypes = createTaskDomainTypes();
  // Domain types initialized

  // Middleware BEFORE DaprServer setup
  app.use(helmet());
  app.use(cors({ origin: config.CORS_ORIGIN }));
  app.use(compression());
  // Custom morgan format to skip repetitive requests
  app.use(morgan((tokens, req, res) => {
    // Skip logging for health checks and actor state checks
    if (req.url?.includes('/health') || req.url?.includes('/dapr/config') || req.url?.includes('/actors/')) {
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


  // CRITICAL: Convert POST to PUT for actor method calls
  // This middleware MUST come before DaprServer setup
  app.use((req, res, next) => {
    if (req.path.includes('/method/') && req.method === 'POST') {
      req.method = 'PUT';
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
  } catch (error) {
    process.exit(1);
  }

  // Create DaprServer and pass the Express app and domain types
  const daprServer = await setupDaprActorsWithApp(app, domainTypes);

  // Start the DaprServer
  await daprServer.start();
  
  console.log(`
🚀 Server is running!
📡 Environment: ${config.NODE_ENV}
🔗 URL: http://localhost:${config.PORT}
🔗 API: http://localhost:${config.PORT}${config.API_PREFIX}
🎭 Dapr App ID: ${config.DAPR_APP_ID}
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

async function setupDaprActorsWithApp(app: express.Express, domainTypes: any) {
  
  // Configure AggregateActorFactory FIRST - before creating DaprServer
  // This ensures the container is initialized before any actor instances are created
  const tempActorProxyFactory = {
    createActorProxy: <T>(): T => {
      throw new Error('Temporary factory - will be replaced after event store init');
    }
  };
  
  AggregateActorFactory.configure(
    domainTypes,
    {}, // service provider
    tempActorProxyFactory,
    {}, // serializationService
    null // eventStore - will be set later
  );

  // Choose storage type based on configuration
  let eventStore: any;
  
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
          throw result.error;
        }
      } catch (error) {
        throw error;
      }
      break;
    }
    
    case 'cosmos': {
      // Initialize Cosmos DB event store
      if (!config.COSMOS_CONNECTION_STRING) {
        throw new Error('COSMOS_CONNECTION_STRING environment variable is required for Cosmos DB storage');
      }
      
      // Use createCosmosEventStore helper function
      const result = await createCosmosEventStore({
        type: StorageProviderType.CosmosDB,
        connectionString: config.COSMOS_CONNECTION_STRING,
        databaseName: config.COSMOS_DATABASE,
        enableLogging: false // Disable verbose logging
      });
      
      if (!result.isOk()) {
        throw result.error;
      }
      
      eventStore = result.value;
      break;
    }
    
    default: {
      throw new Error(`CLAUDE.md violation: Storage type '${config.STORAGE_TYPE}' not supported. Use 'postgres' or 'cosmos' for proper actor implementation. In-memory workarounds are forbidden.`);
    }
  }

  // Create DaprClient for actor proxy factory
  const daprClient = new DaprClient({
    daprHost: "127.0.0.1",
    daprPort: String(config.DAPR_HTTP_PORT)
  });

  // Create actor proxy factory that uses DaprClient with ActorProxyBuilder
  const actorProxyFactory = {
    createActorProxy: <T>(actorId: any, actorType: string): T => {
      const actorIdStr = actorId.id || actorId;
      
      // Use ActorProxyBuilder for all actor types
      if (actorType === 'AggregateEventHandlerActor') {
        // For cross-service actor communication, create a custom proxy using service invocation
        const proxy = {
          appendEventsAsync: async (expectedLastSortableUniqueId: string, events: any[]) => {
            try {
              const result = await daprClient.invoker.invoke(
                'dapr-sample-event-handler', // Target app-id where the actor is hosted
                `/actors/${actorType}/${actorIdStr}/method/appendEventsAsync`,
                HttpMethod.PUT,
                [expectedLastSortableUniqueId, events]
              );
              return result;
            } catch (error) {
              logger.error('Failed to invoke AggregateEventHandlerActor:', error);
              throw error;
            }
          },
          getAllEventsAsync: async () => {
            try {
              const result = await daprClient.invoker.invoke(
                'dapr-sample-event-handler', // Target app-id where the actor is hosted
                `/actors/${actorType}/${actorIdStr}/method/getAllEventsAsync`,
                HttpMethod.PUT,
                []
              );
              return result;
            } catch (error) {
              logger.error('Failed to invoke AggregateEventHandlerActor:', error);
              throw error;
            }
          }
        };
        return proxy as T;
      } else if (actorType === 'AggregateActor') {
        const AggregateActorClass = AggregateActorFactory.createActorClass();
        const builder = new ActorProxyBuilder(AggregateActorClass, daprClient);
        return builder.build(new ActorId(actorIdStr)) as T;
      } else {
        // Unknown actor type
        throw new Error(`Unknown actor type: ${actorType}`);
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

  // Reconfigure actor factory with actual dependencies
  // The domain types were already set earlier, so they'll be preserved
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
  
  // Register all actors explicitly before starting the server
  await daprServer.actor.registerActor(AggregateActorClass);
  
  // Only register AggregateActor in this service
  // AggregateEventHandlerActor is now in a separate service
  
  // Initialize actor runtime
  await daprServer.actor.init();
  
  return daprServer;
}

// Start the server
startServer().catch((error) => {
  process.exit(1);
});