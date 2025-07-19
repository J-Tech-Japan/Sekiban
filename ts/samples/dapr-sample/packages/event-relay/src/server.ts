import express from 'express';
import { DaprClient } from '@dapr/dapr';
import compression from 'compression';
import cors from 'cors';
import helmet from 'helmet';
import morgan from 'morgan';
import { logger } from './utils/logger.js';
import { config } from './config/index.js';
import { errorHandler } from './middleware/error-handler.js';

// PubSub event data interface
interface PubSubEventData {
  id: string;
  aggregateId: string;
  aggregateType: string;
  type: string;
  eventType: string;
  version: number;
  sortKey: string;
  created: string;
  data: any;
  rootPartitionKey?: string;
  tenantId?: string;
}

async function main() {
  const app = express();

  // Middleware
  app.use(helmet());
  app.use(cors());
  app.use(compression());
  app.use(morgan('combined', { stream: { write: (message) => logger.info(message.trim()) } }));
  app.use(express.json({ limit: '10mb' }));

  // Health check
  app.get('/health', (_req, res) => {
    res.json({ status: 'healthy', service: 'event-relay' });
  });

  // Dapr pub/sub subscription endpoint
  app.get('/dapr/subscribe', (_req, res) => {
    logger.info('[PubSub] Subscription endpoint called');
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
    logger.info('[PubSub] Received event:', {
      topic: req.headers['ce-topic'],
      type: req.headers['ce-type'],
      id: req.headers['ce-id'],
      source: req.headers['ce-source']
    });
    logger.info('[PubSub] Request body:', JSON.stringify(req.body, null, 2));
    
    try {
      // Dapr wraps the event in a cloud event envelope
      const eventData = req.body.data || req.body;
      
      // Distribute event to all relevant services
      await distributeEvent(eventData);
      
      // Return 200 OK to acknowledge message
      res.status(200).json({ success: true });
    } catch (error) {
      logger.error('[PubSub] Error processing event:', error);
      // Return 500 to retry later
      res.status(500).json({ 
        error: error instanceof Error ? error.message : 'Unknown error' 
      });
    }
  });

  // Error handling (must be last)
  app.use(errorHandler);

  // Start Express server
  const server = app.listen(config.PORT, () => {
    console.log(`
🚀 Event Relay Service is running!
📡 Environment: ${config.NODE_ENV}
🔗 URL: http://localhost:${config.PORT}
🎭 Dapr App ID: ${config.DAPR_APP_ID}
📨 Subscribed to: sekiban-events
    `);
  });

  // Graceful shutdown
  const gracefulShutdown = async (signal: string) => {
    console.log(`\n${signal} received, starting graceful shutdown...`);
    
    server.close(() => {
      console.log('HTTP server closed');
      process.exit(0);
    });

    // Force shutdown after 30 seconds
    setTimeout(() => {
      console.error('Could not close connections in time, forcefully shutting down');
      process.exit(1);
    }, 30000);
  };

  process.on('SIGTERM', () => gracefulShutdown('SIGTERM'));
  process.on('SIGINT', () => gracefulShutdown('SIGINT'));
}

/**
 * Distribute event to all relevant services
 */
async function distributeEvent(eventData: PubSubEventData) {
  logger.info('[Distributor] Processing event:', {
    type: eventData?.type,
    aggregateType: eventData?.aggregateType,
    aggregateId: eventData?.aggregateId
  });

  // Create DaprClient for invoking services
  const daprClient = new DaprClient({
    daprHost: "127.0.0.1",
    daprPort: String(config.DAPR_HTTP_PORT)
  });

  // Define the services to distribute events to
  const distributionTargets = [
    {
      appId: 'dapr-sample-multi-projector',
      actorType: 'MultiProjectorActor',
      projectorTypes: ['TaskProjector', 'UserProjector', 'WeatherForecastProjector']
    },
    {
      appId: 'dapr-sample-event-handler',
      actorType: 'AggregateEventHandlerActor',
      aggregateTypes: ['Task', 'User', 'WeatherForecast']
    }
  ];

  const promises = [];

  // Distribute to MultiProjectorActors
  const multiProjectorTarget = distributionTargets.find(t => t.appId === 'dapr-sample-multi-projector');
  if (multiProjectorTarget) {
    for (const projectorType of multiProjectorTarget.projectorTypes) {
      const actorId = `aggregatelistprojector-${projectorType.toLowerCase()}`;
      
      promises.push(
        (daprClient.actor as any).invoke(
          multiProjectorTarget.actorType,
          actorId,
          'processEvent',
          eventData
        ).then(() => {
          logger.info(`[Distributor] Successfully sent event to ${multiProjectorTarget.actorType}/${actorId}`);
        }).catch((error: any) => {
          logger.error(`[Distributor] Failed to send event to ${multiProjectorTarget.actorType}/${actorId}:`, error);
        })
      );
    }
  }

  // Distribute to AggregateEventHandlerActors
  const eventHandlerTarget = distributionTargets.find(t => t.appId === 'dapr-sample-event-handler');
  if (eventHandlerTarget && eventData.aggregateType) {
    const actorId = `aggregate-${eventData.aggregateType.toLowerCase()}-eventhandler`;
    
    promises.push(
      (daprClient.actor as any).invoke(
        eventHandlerTarget.actorType,
        actorId,
        'processEvent',
        eventData
      ).then(() => {
        logger.info(`[Distributor] Successfully sent event to ${eventHandlerTarget.actorType}/${actorId}`);
      }).catch((error: any) => {
        logger.error(`[Distributor] Failed to send event to ${eventHandlerTarget.actorType}/${actorId}:`, error);
      })
    );
  }

  // Wait for all distributions to complete
  if (promises.length > 0) {
    await Promise.allSettled(promises);
    logger.info(`[Distributor] Event distribution completed (${promises.length} targets)`);
  } else {
    logger.warn('[Distributor] No targets found for event distribution');
  }
}

// Start the server
main().catch((error) => {
  console.error('Failed to start server:', error);
  process.exit(1);
});