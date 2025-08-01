import { Router } from 'express';
import type { Router as ExpressRouter } from 'express';

export const healthRoutes: ExpressRouter = Router();

healthRoutes.get('/health', (_req, res) => {
  res.json({ 
    status: 'healthy',
    service: 'api-multi-projector',
    timestamp: new Date().toISOString() 
  });
});

healthRoutes.get('/ready', (_req, res) => {
  res.json({ 
    status: 'ready',
    service: 'api-multi-projector',
    timestamp: new Date().toISOString() 
  });
});

// Dapr configuration endpoint - REQUIRED for actor discovery
healthRoutes.get('/dapr/config', (_req, res) => {
  res.json({
    entities: ['MultiProjectorActor'],
    actorIdleTimeout: '1h',
    drainOngoingCallTimeout: '30s',
    drainRebalancedActors: true
  });
});

// Dapr subscription endpoint (even if not using pub/sub)
healthRoutes.get('/dapr/subscribe', (_req, res) => {
  res.json([]);
});