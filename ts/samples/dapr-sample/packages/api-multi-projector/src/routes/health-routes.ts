import { Router } from 'express';

export const healthRoutes = Router();

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