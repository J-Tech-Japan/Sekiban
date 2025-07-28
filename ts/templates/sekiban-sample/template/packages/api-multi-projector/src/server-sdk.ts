import express from 'express';
import cors from 'cors';
import helmet from 'helmet';
import compression from 'compression';
import morgan from 'morgan';
import { Pool } from 'pg';
import { DaprServer, DaprClient, CommunicationProtocolEnum, HttpMethod, ActorProxyBuilder, ActorId } from '@dapr/dapr';
import { MultiProjectorActorFactory, getDaprCradle, MultiProjectorActor } from '@sekiban/dapr';
import { InMemoryEventStore, StorageProviderType } from '@sekiban/core';
import { PostgresEventStore } from '@sekiban/postgres';
import { createTaskDomainTypes } from '@dapr-sample/domain';

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

async function startServer() {
  console.log('Starting Multi-Projector Service with Dapr SDK pub/sub...');

  // Initialize domain types
  const domainTypes = createTaskDomainTypes();

  // Choose storage type based on environment variable or config
  const usePostgres = config.USE_POSTGRES;
  
  let eventStore: any;
  
  if (usePostgres) {
    // Initialize PostgreSQL event store
    console.log('Using PostgreSQL event store');
    const pool = new Pool({
      connectionString: config.DATABASE_URL
    });
    
    eventStore = new PostgresEventStore(pool);
    
    // Initialize the database schema
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
      throw error;
    }
  } else {
    // Initialize in-memory event store
    console.log('Using in-memory event store');
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
      console.log(`Creating actor proxy for ${actorType}/${actorId.id}`);
      const actorIdStr = actorId.id || actorId;
      
      // For now, we only need MultiProjectorActor proxies in this service
      if (actorType === 'MultiProjectorActor') {
        const factory = MultiProjectorActorFactory as unknown as { createActorClass(): typeof MultiProjectorActor };
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
  const serializationService = {
    async deserializeAggregateAsync(surrogate: any) {
      return surrogate;
    },
    async serializeAggregateAsync(aggregate: any) {
      return aggregate;
    }
  };

  // Configure MultiProjectorActorFactory
  const configureMethod = MultiProjectorActorFactory as unknown as {
    configure(domainTypes: any, serviceProvider: any, actorProxyFactory: any, serializationService: any, eventStore: any): void;
  };
  configureMethod.configure(
    domainTypes,
    {}, // service provider
    actorProxyFactory,
    serializationService,
    eventStore
  );

  // Create actor class - use direct reference to avoid type issues
  const MultiProjectorActorClass = MultiProjectorActor as any;

  // Create DaprServer
  const daprServer = new DaprServer({
    serverHost: "127.0.0.1",
    serverPort: String(config.PORT),
    communicationProtocol: CommunicationProtocolEnum.HTTP,
    clientOptions: {
      daprHost: "127.0.0.1",
      daprPort: String(config.DAPR_HTTP_PORT),
      communicationProtocol: CommunicationProtocolEnum.HTTP
    }
  });

  // Register MultiProjectorActor
  await daprServer.actor.registerActor(MultiProjectorActorClass);
  console.log('Registered MultiProjectorActor');

  // Subscribe to events using Dapr SDK
  console.log('Setting up pub/sub subscription...');
  await daprServer.pubsub.subscribe('pubsub', 'sekiban-events', async (data: any) => {
    console.log('[PubSub SDK] Received event:', {
      type: data?.type,
      aggregateType: data?.aggregateType,
      aggregateId: data?.aggregateId,
      dataKeys: Object.keys(data || {})
    });
    
    try {
      await distributeEventToProjectors(data, daprClient);
      console.log('[PubSub SDK] Event processed successfully');
    } catch (error) {
      console.error('[PubSub SDK] Error processing event:', error);
      throw error; // Let Dapr retry
    }
  });

  // Initialize actor runtime
  console.log('Initializing actor runtime...');
  await daprServer.actor.init();
  console.log('Actor runtime initialized');

  // Start the DaprServer
  await daprServer.start();
  
  console.log(`
🚀 MultiProjector Service is running!
📡 Environment: ${config.NODE_ENV}
🔗 URL: http://localhost:${config.PORT}
🔗 API: http://localhost:${config.PORT}${config.API_PREFIX}
🎭 Dapr App ID: ${config.DAPR_APP_ID}
🎭 Actors: MultiProjectorActor
📬 Subscribed to: sekiban-events
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

/**
 * Distribute event to all relevant MultiProjectorActors
 */
async function distributeEventToProjectors(eventData: any, daprClient: DaprClient) {
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

// Start the server
startServer().catch((error) => {
  console.error('Failed to start server:', error);
  process.exit(1);
});