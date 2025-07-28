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
  partitionKey?: string;
  causationId?: string;
  correlationId?: string;
  executedUser?: string;
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
    
    try {
      // Handle CloudEvents format (if content-type is application/cloudevents+json)
      let eventData = req.body;
      let parsedBody = req.body;
      
      // If body came as text, parse it
      if (typeof req.body === 'string') {
        try {
          parsedBody = JSON.parse(req.body);
          eventData = parsedBody.data || parsedBody;
        } catch (e) {
          logger.error('[PubSub] Failed to parse text body:', e);
        }
      }
      
      // If it's CloudEvents format, extract the data
      if (parsedBody.data) {
        eventData = parsedBody.data;
      }
      
      // If it's base64 encoded data (CloudEvents format from Dapr)
      if (parsedBody.data_base64) {
        try {
          const decodedData = Buffer.from(parsedBody.data_base64, 'base64').toString('utf-8');
          const parsedData = JSON.parse(decodedData);
          
          // Extract the nested data if it exists
          if (parsedData.data) {
            eventData = parsedData.data;
          } else {
            eventData = parsedData;
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
        created: eventData.TimeStamp || eventData.timestamp || eventData.createdAt || eventData.created,
        data: eventData.payload || eventData.data || eventData,
        rootPartitionKey: eventData.RootPartitionKey || eventData.rootPartitionKey,
        tenantId: eventData.tenantId,
        partitionKey: eventData.PartitionKey || eventData.partitionKey || eventData.PartitionKeys,
        causationId: eventData.CausationId || eventData.causationId || (eventData.metadata && eventData.metadata.causationId),
        correlationId: eventData.CorrelationId || eventData.correlationId || (eventData.metadata && eventData.metadata.correlationId),
        executedUser: eventData.ExecutedUser || eventData.executedUser || (eventData.metadata && eventData.metadata.executedUser),
        // Additional fields for compatibility
        partitionKeys: eventData.partitionKeys,
        metadata: eventData.metadata
      };
      
      
      
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
  // Use fetch to call actors via Dapr HTTP API
  const daprPort = config.DAPR_HTTP_PORT || 3500;
  const daprHost = '127.0.0.1';

  // Define the services to distribute events to
  const distributionTargets = [
    {
      appId: 'sekiban-sample-multi-projector',
      actorType: 'MultiProjectorActor',
      // These should be aggregate types, not projector class names
      aggregateTypes: ['Task', 'User', 'WeatherForecast'],
      daprPort: '3502' // Multi-projector port
    },
    {
      appId: 'sekiban-sample-event-handler',
      actorType: 'AggregateEventHandlerActor',
      aggregateTypes: ['Task', 'User', 'WeatherForecast'],
      daprPort: '3501' // Event-handler port
    }
  ];

  const promises = [];

  // Distribute to MultiProjectorActors
  const multiProjectorTarget = distributionTargets.find(t => t.appId === 'sekiban-sample-multi-projector');
  if (multiProjectorTarget) {
    for (const aggregateType of multiProjectorTarget.aggregateTypes) {
      const actorId = `aggregatelistprojector-${aggregateType.toLowerCase()}`;
      const url = `http://${daprHost}:${multiProjectorTarget.daprPort}/v1.0/actors/${multiProjectorTarget.actorType}/${actorId}/method/handlePublishedEvent`;
      
      // Wrap event in DaprEventEnvelope format
      // Include both TypeScript and C# field names for compatibility
      const eventEnvelope = {
        event: {
          ...eventData,
          // Ensure C# field names are included
          Id: eventData.id,
          AggregateId: eventData.aggregateId,
          AggregateGroup: eventData.aggregateType,
          SortableUniqueId: eventData.sortKey,
          Version: eventData.version,
          PayloadTypeName: eventData.eventType,
          TimeStamp: eventData.created,
          PartitionKey: eventData.partitionKey,
          CausationId: eventData.causationId,
          CorrelationId: eventData.correlationId,
          ExecutedUser: eventData.executedUser,
          RootPartitionKey: eventData.rootPartitionKey,
          // Include TypeScript field names
          id: eventData.id,
          aggregateId: eventData.aggregateId,
          aggregateType: eventData.aggregateType,
          sortableUniqueId: eventData.sortKey,
          version: eventData.version,
          eventType: eventData.eventType,
          createdAt: eventData.created,
          payload: eventData.data,
          metadata: {
            causationId: eventData.causationId,
            correlationId: eventData.correlationId,
            executedUser: eventData.executedUser
          },
          partitionKeys: {
            aggregateId: eventData.aggregateId,
            group: eventData.aggregateType,
            partitionKey: eventData.partitionKey
          }
        },
        topic: 'sekiban-events',
        pubsubName: 'pubsub'
      };
      
      promises.push(
        fetch(url, {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json'
          },
          body: JSON.stringify(eventEnvelope)
        }).then(async (res) => {
          const responseText = await res.text();
          if (!res.ok) {
            logger.error(`[Distributor] ✗ Failed to send event to ${multiProjectorTarget.actorType}/${actorId}:`, {
              status: res.status,
              statusText: res.statusText,
              body: responseText,
              aggregateType: aggregateType
            });
          } else {
            logger.info(`[Distributor] ✓ Successfully sent event to ${multiProjectorTarget.actorType}/${actorId}`);
          }
        }).catch((error: any) => {
          logger.error(`[Distributor] ✗ Network error calling ${multiProjectorTarget.actorType}/${actorId}:`, error);
        })
      );
    }
  }

  // Distribute to AggregateEventHandlerActors
  const eventHandlerTarget = distributionTargets.find(t => t.appId === 'sekiban-sample-event-handler');
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
        const responseText = await res.text();
        if (!res.ok) {
          logger.error(`[Distributor] ✗ Failed to send event to ${eventHandlerTarget.actorType}/${actorId}:`, {
            status: res.status,
            statusText: res.statusText,
            body: responseText
          });
        } else {
          logger.info(`[Distributor] ✓ Successfully sent event to ${eventHandlerTarget.actorType}/${actorId}`);
        }
      }).catch((error: any) => {
        logger.error(`[Distributor] ✗ Network error calling ${eventHandlerTarget.actorType}/${actorId}:`, error);
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