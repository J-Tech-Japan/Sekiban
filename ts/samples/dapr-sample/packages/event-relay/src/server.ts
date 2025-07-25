import express from 'express';
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
  app.use(express.text({ type: 'application/cloudevents+json' }));

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
          rawPayload: 'true'
        }
      }
    ]);
  });

  // Event handler endpoint
  app.post('/events', async (req, res) => {
    logger.info('[PubSub] ========== RECEIVED EVENT ==========');
    logger.info('[PubSub] CloudEvents headers:', {
      topic: req.headers['ce-topic'],
      type: req.headers['ce-type'],
      id: req.headers['ce-id'],
      source: req.headers['ce-source'],
      contentType: req.headers['content-type']
    });
    logger.info('[PubSub] Request headers:', req.headers);
    logger.info('[PubSub] Request body type:', typeof req.body);
    logger.info('[PubSub] Request body:', JSON.stringify(req.body, null, 2));
    
    try {
      // Handle CloudEvents format (if content-type is application/cloudevents+json)
      let eventData = req.body;
      
      // If body came as text, parse it
      if (typeof req.body === 'string') {
        try {
          const parsed = JSON.parse(req.body);
          eventData = parsed.data || parsed;
        } catch (e) {
          logger.error('[PubSub] Failed to parse text body:', e);
        }
      }
      
      // If it's CloudEvents format, extract the data
      if (req.body.data) {
        eventData = req.body.data;
      }
      
      // If it's base64 encoded data (CloudEvents format from Dapr)
      if (req.body.data_base64) {
        try {
          const decodedData = Buffer.from(req.body.data_base64, 'base64').toString('utf-8');
          eventData = JSON.parse(decodedData);
          logger.info('[PubSub] Decoded data_base64:', JSON.stringify(eventData, null, 2));
          
          // Extract the nested data if it exists
          if (eventData.data) {
            eventData = eventData.data;
          }
        } catch (e) {
          logger.error('[PubSub] Failed to decode data_base64:', e);
        }
      }
      
      // If it's base64 encoded (from CompressedPayloadJson), decode it
      if (eventData.CompressedPayloadJson) {
        try {
          const decodedPayload = Buffer.from(eventData.CompressedPayloadJson, 'base64').toString('utf-8');
          eventData.payload = JSON.parse(decodedPayload);
        } catch (e) {
          logger.error('[PubSub] Failed to decode CompressedPayloadJson:', e);
        }
      }
      
      // Map C# field names to TypeScript field names
      const mappedEventData = {
        id: eventData.Id || eventData.id,
        aggregateId: eventData.AggregateId || eventData.aggregateId,
        aggregateType: eventData.AggregateGroup || eventData.aggregateType,
        type: eventData.PayloadTypeName || eventData.type || eventData.eventType,
        eventType: eventData.PayloadTypeName || eventData.eventType || eventData.type,
        version: eventData.Version || eventData.version,
        sortKey: eventData.SortableUniqueId || eventData.sortableUniqueId || eventData.sortKey,
        created: eventData.TimeStamp || eventData.timestamp || eventData.created,
        data: eventData.payload || eventData.data || eventData,
        rootPartitionKey: eventData.RootPartitionKey || eventData.rootPartitionKey,
        tenantId: eventData.tenantId,
        partitionKey: eventData.PartitionKey || eventData.partitionKey,
        causationId: eventData.CausationId || eventData.causationId,
        correlationId: eventData.CorrelationId || eventData.correlationId,
        executedUser: eventData.ExecutedUser || eventData.executedUser
      };
      
      logger.info('[PubSub] Mapped event data:', {
        id: mappedEventData.id,
        aggregateType: mappedEventData.aggregateType,
        aggregateId: mappedEventData.aggregateId,
        eventType: mappedEventData.eventType,
        version: mappedEventData.version
      });
      
      // Check if this is a Task event
      if (mappedEventData.aggregateType === 'Task') {
        logger.info('[PubSub] *** TASK EVENT DETECTED ***');
        logger.info('[PubSub] Task event details:', JSON.stringify(mappedEventData, null, 2));
      }
      
      // Distribute event to all relevant services
      await distributeEvent(mappedEventData);
      
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
  logger.info('[Distributor] ========== DISTRIBUTING EVENT ==========');
  logger.info('[Distributor] Processing event:', {
    type: eventData?.type,
    aggregateType: eventData?.aggregateType,
    aggregateId: eventData?.aggregateId,
    eventType: eventData?.eventType,
    version: eventData?.version
  });

  // Use fetch to call actors via Dapr HTTP API
  const daprPort = config.DAPR_HTTP_PORT || 3500;
  const daprHost = '127.0.0.1';

  // Define the services to distribute events to
  const distributionTargets = [
    {
      appId: 'dapr-sample-multi-projector',
      actorType: 'MultiProjectorActor',
      projectorTypes: ['TaskProjector', 'UserProjector', 'WeatherForecastProjector'],
      daprPort: '3502' // Multi-projector port
    },
    {
      appId: 'dapr-sample-event-handler',
      actorType: 'AggregateEventHandlerActor',
      aggregateTypes: ['Task', 'User', 'WeatherForecast'],
      daprPort: '3501' // Event-handler port
    }
  ];

  const promises = [];

  // Distribute to MultiProjectorActors
  const multiProjectorTarget = distributionTargets.find(t => t.appId === 'dapr-sample-multi-projector');
  if (multiProjectorTarget) {
    logger.info('[Distributor] Distributing to MultiProjectorActors');
    for (const projectorType of multiProjectorTarget.projectorTypes) {
      const actorId = `aggregatelistprojector-${projectorType.toLowerCase()}`;
      const url = `http://${daprHost}:${multiProjectorTarget.daprPort}/v1.0/actors/${multiProjectorTarget.actorType}/${actorId}/method/processEvent`;
      
      logger.info(`[Distributor] Calling MultiProjectorActor:`, {
        projectorType,
        actorId,
        url,
        eventAggregateType: eventData.aggregateType
      });
      
      // Check if this is TaskProjector processing a Task event
      if (projectorType === 'TaskProjector' && eventData.aggregateType === 'Task') {
        logger.info('[Distributor] *** SENDING TASK EVENT TO TASK PROJECTOR ***');
      }
      
      promises.push(
        fetch(url, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json'
          },
          body: JSON.stringify(eventData)
        }).then(async (res) => {
          const responseText = await res.text();
          if (res.ok) {
            logger.info(`[Distributor] ✓ Successfully sent event to ${multiProjectorTarget.actorType}/${actorId}`, {
              status: res.status,
              response: responseText
            });
          } else {
            logger.error(`[Distributor] ✗ Failed to send event to ${multiProjectorTarget.actorType}/${actorId}:`, {
              status: res.status,
              statusText: res.statusText,
              body: responseText
            });
          }
        }).catch((error: any) => {
          logger.error(`[Distributor] ✗ Network error calling ${multiProjectorTarget.actorType}/${actorId}:`, error);
        })
      );
    }
  } else {
    logger.warn('[Distributor] No MultiProjectorActor target found!');
  }

  // Distribute to AggregateEventHandlerActors
  const eventHandlerTarget = distributionTargets.find(t => t.appId === 'dapr-sample-event-handler');
  if (eventHandlerTarget && eventData.aggregateType) {
    const actorId = `aggregate-${eventData.aggregateType.toLowerCase()}-eventhandler`;
    const url = `http://${daprHost}:${eventHandlerTarget.daprPort}/v1.0/actors/${eventHandlerTarget.actorType}/${actorId}/method/processEvent`;
    
    promises.push(
      fetch(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json'
        },
        body: JSON.stringify(eventData)
      }).then(async (res) => {
        if (res.ok) {
          logger.info(`[Distributor] Successfully sent event to ${eventHandlerTarget.actorType}/${actorId}`);
        } else {
          const text = await res.text();
          logger.error(`[Distributor] Failed to send event to ${eventHandlerTarget.actorType}/${actorId}:`, {
            status: res.status,
            statusText: res.statusText,
            body: text
          });
        }
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