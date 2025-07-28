import { Router, Request, Response } from 'express';
import type { Router as ExpressRouter } from 'express';
import { config } from '../config/index.js';

const router: ExpressRouter = Router();

// Health check
router.get('/health', (req: Request, res: Response) => {
  res.json({
    status: 'healthy',
    timestamp: new Date().toISOString(),
    environment: config.NODE_ENV,
    appId: config.DAPR_APP_ID
  });
});

// Readiness check
router.get('/ready', async (req: Request, res: Response) => {
  try {
    // In a real app, check database connection, Dapr sidecar, etc.
    res.json({
      status: 'ready',
      timestamp: new Date().toISOString()
    });
  } catch (error) {
    res.status(503).json({
      status: 'not ready',
      timestamp: new Date().toISOString(),
      error: error instanceof Error ? error.message : 'Unknown error'
    });
  }
});

// Dapr configuration endpoint
router.get('/dapr/config', (req: Request, res: Response) => {
  res.json({
    entities: [config.DAPR_ACTOR_TYPE],
    actorIdleTimeout: '1h',
    drainOngoingCallTimeout: '30s',
    drainRebalancedActors: true
  });
});

// Dapr subscription endpoint
router.get('/dapr/subscribe', (req: Request, res: Response) => {
  res.json([
    {
      pubsubname: config.DAPR_PUBSUB_NAME,
      topic: config.DAPR_EVENT_TOPIC,
      route: '/events'
    }
  ]);
});

export { router as healthRoutes };