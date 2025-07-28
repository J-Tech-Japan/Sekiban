import express from 'express';
import cors from 'cors';
import helmet from 'helmet';
import compression from 'compression';
import morgan from 'morgan';
import pg from 'pg';
import { config } from './config/index.js';
import { cleanup } from './setup/executor.js';
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
import { InMemoryEventStore, StorageProviderType } from '@sekiban/core';
import { PostgresEventStore } from '@sekiban/postgres';
import { createCosmosEventStore } from '@sekiban/cosmos';

const { Pool } = pg;
import { createTaskDomainTypes } from '@dapr-sample/domain';


async function startServer() {
  const app = express();

  // Middleware BEFORE DaprServer setup
  app.use(helmet());
  app.use(cors({ origin: config.CORS_ORIGIN }));
  app.use(compression());
  app.use(morgan(config.NODE_ENV === 'production' ? 'combined' : 'dev'));
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

  // Executor will be initialized in setupDaprActorsWithApp

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
🎭 Actors: AggregateActor
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
  console.log('Setting up Dapr actors with storage type:', config.STORAGE_TYPE);

  // Initialize domain types
  const domainTypes = createTaskDomainTypes();

  // Choose storage type based on configuration
  let eventStore: any;
  
  switch (config.STORAGE_TYPE) {
    case 'postgres': {
      // Initialize PostgreSQL event store
      console.log('Initializing PostgreSQL event store...');
      console.log('Database URL:', config.DATABASE_URL);
      
      const pool = new Pool({
        connectionString: config.DATABASE_URL
      });
      
      eventStore = new PostgresEventStore(pool);
      
      // Initialize the database schema
      try {
        const result = await eventStore.initialize();
        if (!result.isOk()) {
          console.error('Failed to initialize PostgreSQL schema:', result.error);
          throw result.error;
        }
        console.log('PostgreSQL event store initialized successfully');
      } catch (error) {
        console.error('Failed to initialize PostgreSQL:', error);
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
        enableLogging: true
      });
      
      if (!result.isOk()) {
        console.error('Failed to create Cosmos DB event store:', result.error);
        throw result.error;
      }
      
      eventStore = result.value;
      break;
    }
    
    case 'inmemory':
    default: {
      // Initialize in-memory event store
      eventStore = new InMemoryEventStore({
        type: StorageProviderType.InMemory,
        enableLogging: config.NODE_ENV === 'development'
      });
      break;
    }
  }

  // Create DaprClient for actor proxy factory
  const daprClient = new DaprClient({
    daprHost: "127.0.0.1",
    daprPort: String(config.DAPR_HTTP_PORT)
  });

  // Create actor proxy factory that uses DaprClient with ActorProxyBuilder
  ￥
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
  
  // Set the event store in executor for later use
  const { setEventStore, createExecutor } = await import('./setup/executor.js');
  setEventStore(eventStore);
  
  // Initialize executor with the configured event store
  await createExecutor();

  // Create actor classes before DaprServer initialization
  const AggregateActorClass = AggregateActorFactory.createActorClass();

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
  console.error('Failed to start server:', error);
  process.exit(1);
});